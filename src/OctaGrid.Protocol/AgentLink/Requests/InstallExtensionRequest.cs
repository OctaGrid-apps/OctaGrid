using OctaGrid.Protocol.AgentLink.Replies;

namespace OctaGrid.Protocol.AgentLink.Requests;

/// <summary>
/// Installs an extension package. The package is uploaded to the agent first (<see cref="BeginUploadRequest"/>,
/// <see cref="UploadChunkRequest"/>); this request ends that upload. Nothing from the package runs until it is approved.
/// </summary>
/// <param name="UploadId">The upload that holds the package (<c>.octaext</c> or <c>.zip</c>).</param>
public sealed record InstallExtensionRequest(Guid UploadId) : AgentRequest<ExtensionChangeReply>;
