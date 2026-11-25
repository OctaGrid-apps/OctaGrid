using FluentFTP;
using FluentFTP.BouncyCastle;
using Microsoft.Extensions.Logging;

namespace OctaGrid.Connectors.BambuLab;

/// <summary>FluentFTP settings for the printer's implicit FTPS server (LAN mode, port 990).</summary>
internal static class BambuFtps
{
    public const int Port = 990;
    public const string Username = "bblp";

    /// <summary>Creates the configuration every FTPS connection to a Bambu printer uses.</summary>
    /// <remarks>
    /// The printer's vsftpd requires the data connection to resume the control connection's TLS session
    /// (522 "session reuse required" otherwise), which SslStream does not do even with TLS 1.2 pinned.
    /// TLS therefore runs on Bouncy Castle, which hands the control session to every data connection
    /// and is fully managed, so it works on every OS and architecture.
    /// </remarks>
    /// <param name="diagnostic">Receives Bouncy Castle's handshake and resumption notes; they hold no secrets.</param>
    public static FtpConfig CreateConfig(Action<string>? diagnostic = null) => new()
    {
        EncryptionMode = FtpEncryptionMode.Implicit,
        CustomStream = typeof(BouncyCastleFtpStream),
        CustomStreamConfig = new BouncyCastleFtpConfig
        {
            // Fail a transfer that would not resume, rather than sending it and getting 522.
            RequireSessionResumption = true,
            // Printer firmware may not offer Extended Master Secret; without this the session is never
            // offered for resumption. The certificate is not validated either, so this weakens nothing.
            AllowLegacyResumption = true,
            Diagnostic = diagnostic,
        },
        // LAN-mode printers present a self-signed certificate.
        ValidateAnyCertificate = true,
        DataConnectionEncryption = true,
        SocketKeepAlive = true,
    };

    /// <summary>Creates a client for the printer at <paramref name="host"/>; the caller connects and disposes it.</summary>
    public static AsyncFtpClient CreateClient(string host, string accessCode, ILogger logger) =>
        new(host, Username, accessCode, Port, CreateConfig(message => logger.LogDebug("Bambu FTPS TLS: {TlsDiagnostic}", message)));
}
