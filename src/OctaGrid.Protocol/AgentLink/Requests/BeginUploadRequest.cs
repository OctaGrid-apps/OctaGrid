using OctaGrid.Protocol.AgentLink.Replies;

namespace OctaGrid.Protocol.AgentLink.Requests;

/// <summary>
/// Starts an upload to the agent. The content follows in <see cref="UploadChunkRequest"/> messages, and the
/// request that uses it (<see cref="StorePrinterFileRequest"/> or <see cref="InstallExtensionRequest"/>) ends it.
/// </summary>
/// <param name="Length">The total size in bytes when it is known, so the agent can refuse what is too large before receiving it.</param>
public sealed record BeginUploadRequest(long? Length) : AgentRequest<UploadStartedReply>;
