using OctaGrid.Protocol.AgentLink.Replies;

namespace OctaGrid.Protocol.AgentLink.Requests;

/// <summary>Starts printing a file that is on the printer.</summary>
/// <param name="PrinterId">The printer.</param>
/// <param name="FileName">The file to print, as the printer lists it.</param>
public sealed record StartPrintRequest(Guid PrinterId, string FileName) : AgentRequest<AgentAck>;
