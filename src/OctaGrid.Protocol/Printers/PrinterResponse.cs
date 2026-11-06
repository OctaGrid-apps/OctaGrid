namespace OctaGrid.Protocol.Printers;

/// <summary>A printer as returned to clients.</summary>
/// <param name="Id">Printer id.</param>
/// <param name="Name">Name shown to users.</param>
/// <param name="ConnectorId">Id of the connector that talks to the printer.</param>
/// <param name="Settings">Values for the connector's non-secret settings, by key.</param>
/// <param name="Secrets">Values for the connector's secret settings, by key.</param>
/// <param name="ConnectionState">Last known connection state.</param>
/// <param name="Enabled">Whether the agent connects to the printer.</param>
/// <param name="Version">Version used to ignore out-of-date change notifications.</param>
public record PrinterResponse(
    Guid Id,
    string Name,
    string ConnectorId,
    IReadOnlyDictionary<string, string> Settings,
    IReadOnlyDictionary<string, string> Secrets,
    string ConnectionState,
    bool Enabled,
    long Version);
