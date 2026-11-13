using OctaGrid.Protocol.AgentLink.Replies;

namespace OctaGrid.Protocol.AgentLink.Requests;

/// <summary>Resumes a paused print job.</summary>
/// <param name="PrinterId">The printer.</param>
public sealed record ResumePrintRequest(Guid PrinterId) : AgentRequest<AgentAck>;
