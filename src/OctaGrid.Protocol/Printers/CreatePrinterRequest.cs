namespace OctaGrid.Protocol.Printers;

/// <summary>Creates a printer.</summary>
/// <param name="Name">Name shown to users.</param>
/// <param name="ConnectorId">Id of the connector that talks to the printer.</param>
/// <param name="Settings">Values for the connector's declared settings that are not secret, by key.</param>
/// <param name="Secrets">Values for the connector's secret settings (passwords, access codes, tokens), by key.</param>
/// <param name="Enabled">Whether the agent should connect to the printer.</param>
public record CreatePrinterRequest(
    string Name,
    string ConnectorId,
    IReadOnlyDictionary<string, string> Settings,
    IReadOnlyDictionary<string, string> Secrets,
    bool Enabled = true);
