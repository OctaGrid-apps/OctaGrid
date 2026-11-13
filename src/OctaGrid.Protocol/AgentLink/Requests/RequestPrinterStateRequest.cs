using OctaGrid.Protocol.AgentLink.Replies;

namespace OctaGrid.Protocol.AgentLink.Requests;

/// <summary>
/// Asks the agent to report the current state of printers again, for a client that has just started
/// showing them. The state arrives as telemetry notifications, not in the reply.
/// </summary>
/// <param name="PrinterIds">The printers.</param>
public sealed record RequestPrinterStateRequest(IReadOnlyList<Guid> PrinterIds) : AgentRequest<AgentAck>;
