using System.Net.Sockets;
using System.Security.Authentication;
using FluentFTP;
using FluentFTP.BouncyCastle;
using FluentFTP.Streams;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Connectors.BambuLab;

namespace OctaGrid.Connectors.Tests;

public class BambuFtpsTests
{
    [Fact]
    public void CreateConfig_UsesImplicitFtpsOverBouncyCastleWithEncryptedData()
    {
        var config = BambuFtps.CreateConfig();

        Assert.Equal(FtpEncryptionMode.Implicit, config.EncryptionMode);
        Assert.True(config.DataConnectionEncryption);
        Assert.Equal(typeof(BouncyCastleFtpStream), config.CustomStream);
        var tls = Assert.IsType<BouncyCastleFtpConfig>(config.CustomStreamConfig);
        Assert.True(tls.RequireSessionResumption);
    }

    [Fact]
    public void CreateClient_TargetsPort990AsTheLanUser()
    {
        using var client = BambuFtps.CreateClient("192.0.2.1", "12345678", NullLogger.Instance);

        Assert.Equal(990, client.Port);
        Assert.Equal("bblp", client.Credentials.UserName);
        Assert.Equal(typeof(BouncyCastleFtpStream), client.Config.CustomStream);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DataConnection_ResumesTheControlConnectionsTlsSession(bool serverUsesExtendedMasterSecret)
    {
        using var server = new FakeFtpsTlsServer(useExtendedMasterSecret: serverUsesExtendedMasterSecret);
        var ct = TestContext.Current.CancellationToken;

        using var control = await OpenAsync(server, isControl: true, controlStream: null, ct);
        Assert.False(await control.Resumed);

        using var data = await OpenAsync(server, isControl: false, control.Stream, ct);
        Assert.True(await data.Resumed);
        Assert.Equal(SslProtocols.Tls12, data.Stream.GetSslProtocol());
    }

    [Fact]
    public async Task DataConnection_FailsBeforeTransferring_WhenTheServerDoesNotResume()
    {
        using var server = new FakeFtpsTlsServer(resumeSessions: false);
        var ct = TestContext.Current.CancellationToken;

        using var control = await OpenAsync(server, isControl: true, controlStream: null, ct);

        await Assert.ThrowsAsync<AuthenticationException>(() => OpenAsync(server, isControl: false, control.Stream, ct));
    }

    /// <summary>Runs the TLS handshake FluentFTP would run for one connection, with Bambu's configuration.</summary>
    private static async Task<Connection> OpenAsync(
        FakeFtpsTlsServer server, bool isControl, IFtpStream? controlStream, CancellationToken cancellationToken)
    {
        var config = BambuFtps.CreateConfig();
        config.ValidateCertificateRevocation = false;
        var ftpClient = new AsyncFtpClient("127.0.0.1", "bblp", "12345678", BambuFtps.Port, config);
        var (socket, resumed) = await server.ConnectAsync(cancellationToken);
        var stream = new BouncyCastleFtpStream();
        try
        {
            // The validation callback stands in for FluentFTP's, which accepts any certificate here.
            await Task.Run(() => stream.Init(
                ftpClient, "127.0.0.1", socket, (_, _, _, _) => true, isControl, controlStream!, config.CustomStreamConfig),
                cancellationToken);

            var tls = stream.GetBaseStream();
            await tls.WriteAsync(new byte[] { 42 }, cancellationToken);
            var echo = new byte[1];
            Assert.Equal(1, await tls.ReadAsync(echo, cancellationToken));
            Assert.Equal(42, echo[0]);

            return new Connection(stream, socket, ftpClient, resumed);
        }
        catch
        {
            stream.Dispose();
            socket.Dispose();
            ftpClient.Dispose();
            throw;
        }
    }

    private sealed record Connection(BouncyCastleFtpStream Stream, Socket Socket, AsyncFtpClient Client, Task<bool> Resumed)
        : IDisposable
    {
        public void Dispose()
        {
            Stream.Dispose();
            Socket.Dispose();
            Client.Dispose();
        }
    }
}
