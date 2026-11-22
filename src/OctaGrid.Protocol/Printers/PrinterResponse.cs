using OctaGrid.Extensions.Models;

namespace OctaGrid.Protocol.Printers;

/// <summary>A printer as returned to clients.</summary>
/// <param name="Id">Printer id.</param>
/// <param name="Name">Name shown to users.</param>
/// <param name="ConnectorId">Id of the connector that talks to the printer.</param>
/// <param name="Settings">Values for the connector's non-secret settings, by key.</param>
/// <param name="SecretKeys">
/// The keys of the connector's secret settings that have a saved value. Secret values are write-only:
/// they are never returned to a client.
/// </param>
/// <param name="Enabled">Whether the agent connects to the printer.</param>
/// <param name="Version">Version used to ignore out-of-date change notifications.</param>
/// <param name="AgentId">The agent that connects to the printer, or null when none is assigned.</param>
/// <param name="State">
/// The last state the printer's agent reported, or null when it has reported none.
/// The live state is in the printer's telemetry.
/// </param>
public record PrinterResponse(
    Guid Id,
    string Name,
    string ConnectorId,
    IReadOnlyDictionary<string, string> Settings,
    IReadOnlyList<string> SecretKeys,
    bool Enabled,
    long Version,
    Guid? AgentId = null,
    UniversalPrinterState? State = null);
