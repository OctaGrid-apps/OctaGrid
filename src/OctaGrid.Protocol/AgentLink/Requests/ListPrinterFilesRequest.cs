using OctaGrid.Protocol.AgentLink.Replies;

namespace OctaGrid.Protocol.AgentLink.Requests;

/// <summary>Asks for the files stored on a printer.</summary>
/// <param name="PrinterId">The printer.</param>
public sealed record ListPrinterFilesRequest(Guid PrinterId) : AgentRequest<PrinterFilesReply>;
