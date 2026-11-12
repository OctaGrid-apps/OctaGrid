using OctaGrid.Protocol.AgentLink.Replies;

namespace OctaGrid.Protocol.AgentLink.Requests;

/// <summary>Stops the print job. The job cannot be resumed afterwards.</summary>
/// <param name="PrinterId">The printer.</param>
public sealed record StopPrintRequest(Guid PrinterId) : AgentRequest<AgentAck>;
