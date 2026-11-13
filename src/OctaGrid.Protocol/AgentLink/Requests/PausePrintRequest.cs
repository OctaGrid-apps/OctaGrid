using OctaGrid.Protocol.AgentLink.Replies;

namespace OctaGrid.Protocol.AgentLink.Requests;

/// <summary>Pauses the running print job.</summary>
/// <param name="PrinterId">The printer.</param>
public sealed record PausePrintRequest(Guid PrinterId) : AgentRequest<AgentAck>;
