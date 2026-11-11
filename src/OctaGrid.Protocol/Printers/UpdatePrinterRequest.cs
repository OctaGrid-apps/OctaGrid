namespace OctaGrid.Protocol.Printers;

/// <summary>Replaces a printer's name, connector and connector settings.</summary>
/// <param name="Name">Name shown to users.</param>
/// <param name="ConnectorId">Id of the connector that talks to the printer.</param>
/// <param name="Settings">Values for the connector's declared settings that are not secret, by key.</param>
/// <param name="Secrets">
/// Changes to the connector's secret settings, by key. A client cannot read saved secrets, so this is not the
/// full set: a key with a value saves that value, a key with an empty value removes the saved one, and a key
/// that is left out keeps what is saved. When <paramref name="ConnectorId"/> changes, saved secrets that are
/// not listed here are removed, because they belong to the previous connector.
/// </param>
/// <param name="Enabled">Whether the agent should connect to the printer.</param>
public record UpdatePrinterRequest(
    string? Name,
    string? ConnectorId,
    IReadOnlyDictionary<string, string>? Settings,
    IReadOnlyDictionary<string, string>? Secrets,
    bool Enabled = true);
