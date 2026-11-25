using System.Collections.Concurrent;
using System.Net.Sockets;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using Org.BouncyCastle.X509;

namespace OctaGrid.Connectors.Tests;

/// <summary>
/// The TLS side of a printer's FTPS server, over a Unix domain socket (no network): it keeps a session
/// cache like vsftpd and reports whether each connection resumed a session.
/// </summary>
internal sealed class FakeFtpsTlsServer : IDisposable
{
    private static readonly Lazy<(X509Certificate Certificate, AsymmetricKeyParameter Key)> Identity = new(CreateIdentity);

    private readonly ConcurrentDictionary<string, TlsSession> _sessions = new();
    private readonly string _path;
    private readonly Socket _listener;

    public FakeFtpsTlsServer(bool resumeSessions = true, bool useExtendedMasterSecret = true)
    {
        ResumeSessions = resumeSessions;
        UseExtendedMasterSecret = useExtendedMasterSecret;
        _path = Path.Combine(Path.GetTempPath(), $"og-ftps-{Guid.NewGuid():N}.sock");
        _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _listener.Bind(new UnixDomainSocketEndPoint(_path));
        _listener.Listen(4);
    }

    public bool ResumeSessions { get; }

    public bool UseExtendedMasterSecret { get; }

    /// <summary>Connects a client socket and starts the server's handshake on it.</summary>
    /// <returns>The client socket and a task that completes with whether the server resumed a session.</returns>
    public async Task<(Socket Client, Task<bool> Resumed)> ConnectAsync(CancellationToken cancellationToken)
    {
        var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        var accept = _listener.AcceptAsync(cancellationToken).AsTask();
        await client.ConnectAsync(new UnixDomainSocketEndPoint(_path), cancellationToken);
        var serverSocket = await accept;

        var resumed = Task.Run(() =>
        {
            var server = new SessionCachingServer(this);
            var protocol = new TlsServerProtocol(new NetworkStream(serverSocket, ownsSocket: true));
            protocol.Accept(server);
            var wasResumed = server.Context.SecurityParameters.IsResumedSession;

            // Echo one byte so the test can prove application data flows over the TLS stream.
            var buffer = new byte[1];
            if (protocol.Stream.Read(buffer, 0, 1) == 1)
            {
                protocol.Stream.Write(buffer, 0, 1);
            }

            return wasResumed;
        }, cancellationToken);

        return (client, resumed);
    }

    public void Dispose()
    {
        _listener.Dispose();
        File.Delete(_path);
    }

    private static (X509Certificate, AsymmetricKeyParameter) CreateIdentity()
    {
        var keys = new RsaKeyPairGenerator();
        keys.Init(new KeyGenerationParameters(new SecureRandom(), 2048));
        var pair = keys.GenerateKeyPair();

        var name = new X509Name("CN=fake-printer");
        var generator = new X509V3CertificateGenerator();
        generator.SetSerialNumber(BigInteger.One);
        generator.SetIssuerDN(name);
        generator.SetSubjectDN(name);
        generator.SetNotBefore(DateTime.UtcNow.AddDays(-1));
        generator.SetNotAfter(DateTime.UtcNow.AddDays(1));
        generator.SetPublicKey(pair.Public);
        var certificate = generator.Generate(new Asn1SignatureFactory("SHA256WITHRSA", pair.Private));
        return (certificate, pair.Private);
    }

    private sealed class SessionCachingServer : DefaultTlsServer
    {
        private readonly FakeFtpsTlsServer _owner;

        public SessionCachingServer(FakeFtpsTlsServer owner)
            : base(new BcTlsCrypto(new SecureRandom()))
        {
            _owner = owner;
        }

        public TlsServerContext Context => m_context;

        public override TlsSession? GetSessionToResume(byte[] sessionID) =>
            _owner.ResumeSessions && _owner._sessions.TryGetValue(Convert.ToHexString(sessionID), out var session) ? session : null;

        public override byte[] GetNewSessionID() =>
            SecureRandom.GetNextBytes(new SecureRandom(), 32);

        public override bool ShouldUseExtendedMasterSecret() => _owner.UseExtendedMasterSecret;

        public override void NotifyHandshakeComplete()
        {
            base.NotifyHandshakeComplete();
            if (m_context.Session is { IsResumable: true } session)
            {
                _owner._sessions[Convert.ToHexString(session.SessionID)] = session;
            }
        }

        protected override ProtocolVersion[] GetSupportedVersions() => ProtocolVersion.TLSv12.Only();

        protected override TlsCredentialedSigner GetRsaSignerCredentials()
        {
            var crypto = (BcTlsCrypto)Crypto;
            var certificate = new Certificate([crypto.CreateCertificate(Identity.Value.Certificate.GetEncoded())]);
            return new BcDefaultTlsCredentialedSigner(
                new TlsCryptoParameters(m_context),
                crypto,
                Identity.Value.Key,
                certificate,
                new SignatureAndHashAlgorithm(HashAlgorithm.sha256, SignatureAlgorithm.rsa));
        }
    }
}
