namespace OctaGrid.Application.Secrets;

/// <summary>
/// Keeps the secret settings of printers (passwords, access codes, API keys) outside the database.
/// Local mode uses the operating system's credential store. Values are never logged and never returned to clients.
/// </summary>
public interface IPrinterSecretStore
{
    /// <summary>The secrets saved for a printer, by key. Empty when there are none.</summary>
    Task<IReadOnlyDictionary<string, string>> GetAsync(Guid printerId, CancellationToken cancellationToken);

    /// <summary>Replaces everything saved for a printer. An empty map removes all of it.</summary>
    Task SetAsync(Guid printerId, IReadOnlyDictionary<string, string> secrets, CancellationToken cancellationToken);
}
