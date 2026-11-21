using OctaGrid.Protocol.AgentLink.Replies;

namespace OctaGrid.Protocol.AgentLink.Requests;

/// <summary>
/// Tells the agent which of its printers some client is showing, so it sends telemetry for those only. Each request
/// replaces the previous set; an empty set stops all telemetry. The agent keeps recording the history of every printer
/// either way, and starts with an empty set.
/// </summary>
/// <param name="PrinterIds">Every printer at least one client is showing.</param>
public sealed record SetWatchedPrintersRequest(IReadOnlyList<Guid> PrinterIds) : AgentRequest<AgentAck>;
