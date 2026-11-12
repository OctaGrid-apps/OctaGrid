using OctaGrid.Protocol.AgentLink.Replies;

namespace OctaGrid.Protocol.AgentLink.Requests;

/// <summary>Asks for the preview image of a file on a printer.</summary>
/// <param name="PrinterId">The printer.</param>
/// <param name="FileName">The file, as the printer lists it.</param>
public sealed record GetPrintThumbnailRequest(Guid PrinterId, string FileName) : AgentRequest<PrintThumbnailReply>;
