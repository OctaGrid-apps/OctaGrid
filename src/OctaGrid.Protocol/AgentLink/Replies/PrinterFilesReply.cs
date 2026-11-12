using OctaGrid.Extensions.Models;

namespace OctaGrid.Protocol.AgentLink.Replies;

/// <summary>The files stored on a printer.</summary>
/// <param name="Files">The files.</param>
public sealed record PrinterFilesReply(IReadOnlyList<PrintMetadata> Files) : AgentReply;
