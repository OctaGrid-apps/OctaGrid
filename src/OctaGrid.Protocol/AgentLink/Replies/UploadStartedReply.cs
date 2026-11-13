namespace OctaGrid.Protocol.AgentLink.Replies;

/// <summary>An upload the agent is ready to receive.</summary>
/// <param name="UploadId">Identifies the upload in the messages that follow.</param>
/// <param name="MaxChunkSize">The most bytes the agent accepts in one piece.</param>
public sealed record UploadStartedReply(Guid UploadId, int MaxChunkSize) : AgentReply;
