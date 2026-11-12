using OctaGrid.Protocol.AgentLink.Replies;

namespace OctaGrid.Protocol.AgentLink.Requests;

/// <summary>Discards an upload that will not be used.</summary>
/// <param name="UploadId">The upload.</param>
public sealed record AbortUploadRequest(Guid UploadId) : AgentRequest<AgentAck>;
