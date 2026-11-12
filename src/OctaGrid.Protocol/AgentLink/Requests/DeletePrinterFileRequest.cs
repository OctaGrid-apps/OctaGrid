using OctaGrid.Protocol.AgentLink.Replies;

namespace OctaGrid.Protocol.AgentLink.Requests;

/// <summary>Deletes a file from a printer.</summary>
/// <param name="PrinterId">The printer.</param>
/// <param name="FileName">The file, as the printer lists it.</param>
public sealed record DeletePrinterFileRequest(Guid PrinterId, string FileName) : AgentRequest<AgentAck>;
