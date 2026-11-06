namespace OctaGrid.Client.Models;

/// <param name="Id">Printer id.</param>
/// <param name="Name">Name shown to users.</param>
/// <param name="ConnectorId">Id of the connector that talks to the printer.</param>
/// <param name="ConnectorName">The connector's display name, or its id when the connector is not available.</param>
/// <param name="Address">The printer's address, taken from the connector's address setting.</param>
/// <param name="ConnectionState">Last known connection state.</param>
/// <param name="Settings">Non-secret settings, by key.</param>
/// <param name="Secrets">Secret settings, by key.</param>
/// <param name="Enabled">Whether the agent connects to the printer.</param>
/// <param name="Version">Version used to ignore out-of-date change notifications.</param>
public record PrinterListItem(
    Guid Id,
    string Name,
    string ConnectorId,
    string ConnectorName,
    string Address,
    string ConnectionState,
    IReadOnlyDictionary<string, string> Settings,
    IReadOnlyDictionary<string, string> Secrets,
    bool Enabled,
    long Version) : IPrinterListItem;
