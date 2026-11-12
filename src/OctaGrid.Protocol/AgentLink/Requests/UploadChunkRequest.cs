using OctaGrid.Protocol.AgentLink.Replies;

namespace OctaGrid.Protocol.AgentLink.Requests;

/// <summary>One piece of an upload.</summary>
/// <param name="UploadId">The upload, from <see cref="UploadStartedReply"/>.</param>
/// <param name="Sequence">The position of this piece, starting at 0. Pieces are sent in order.</param>
/// <param name="Data">The bytes of this piece.</param>
public sealed record UploadChunkRequest(Guid UploadId, int Sequence, byte[] Data) : AgentRequest<AgentAck>;
