using OctaGrid.Protocol.AgentLink.Replies;

namespace OctaGrid.Protocol.AgentLink.Requests;

/// <summary>
/// Asks for the full metadata of files a listing returned with <c>DetailsPending</c> set. The reply lists the files
/// the printer could describe, in any order.
/// </summary>
/// <param name="PrinterId">The printer.</param>
/// <param name="FileNames">The files, as the listing named them.</param>
public sealed record GetPrinterFileDetailsRequest(Guid PrinterId, IReadOnlyList<string> FileNames) : AgentRequest<PrinterFilesReply>;
