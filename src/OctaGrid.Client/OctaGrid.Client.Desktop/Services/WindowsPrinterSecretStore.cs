using Meziantou.Framework.Win32;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Application.Secrets;

namespace OctaGrid.Client.Desktop.Services;

/// <summary>
/// Keeps printer secrets in the Windows Credential Manager, one credential per secret, named
/// <c>OctaGrid/printers/&lt;printer id&gt;/&lt;key&gt;</c>. Windows encrypts them for the signed-in user.
/// One credential per secret keeps each value well inside the size limit of a credential.
/// </summary>
internal sealed class WindowsPrinterSecretStore : IPrinterSecretStore
{
    private const string Prefix = "OctaGrid/printers/";
    private readonly ILogger<WindowsPrinterSecretStore> _logger;

    public WindowsPrinterSecretStore(ILogger<WindowsPrinterSecretStore>? logger = null)
    {
        _logger = logger ?? NullLogger<WindowsPrinterSecretStore>.Instance;
    }

    public Task<IReadOnlyDictionary<string, string>> GetAsync(Guid printerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var prefix = PrinterPrefix(printerId);
        var secrets = new Dictionary<string, string>();
        foreach (var credential in CredentialManager.EnumerateCredentials(prefix + "*"))
        {
            if (credential.Password is { } value)
            {
                secrets[credential.ApplicationName[prefix.Length..]] = value;
            }
        }

        return Task.FromResult<IReadOnlyDictionary<string, string>>(secrets);
    }

    public Task SetAsync(Guid printerId, IReadOnlyDictionary<string, string> secrets, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var prefix = PrinterPrefix(printerId);
        var removed = 0;
        foreach (var credential in CredentialManager.EnumerateCredentials(prefix + "*"))
        {
            if (!secrets.ContainsKey(credential.ApplicationName[prefix.Length..]))
            {
                CredentialManager.DeleteCredential(credential.ApplicationName);
                removed++;
            }
        }

        foreach (var (key, value) in secrets)
        {
            CredentialManager.WriteCredential(prefix + key, key, value, CredentialPersistence.LocalMachine);
        }

        // Only counts are logged: neither keys nor values belong in a log.
        _logger.LogDebug("Saved {SavedCount} and removed {RemovedCount} secret(s) for printer {PrinterId}", secrets.Count, removed, printerId);
        return Task.CompletedTask;
    }

    private static string PrinterPrefix(Guid printerId) => $"{Prefix}{printerId:N}/";
}
