using OctaGrid.Protocol.AgentLink.Replies;

namespace OctaGrid.Protocol.AgentLink.Requests;

/// <summary>
/// Sends an uploaded file on to a printer. The content is uploaded to the agent first
/// (<see cref="BeginUploadRequest"/>, <see cref="UploadChunkRequest"/>); this request ends that upload.
/// </summary>
/// <param name="PrinterId">The printer that gets the file.</param>
/// <param name="UploadId">The upload that holds the content.</param>
/// <param name="FileName">The name the file gets on the printer.</param>
public sealed record StorePrinterFileRequest(Guid PrinterId, Guid UploadId, string FileName) : AgentRequest<AgentAck>;
