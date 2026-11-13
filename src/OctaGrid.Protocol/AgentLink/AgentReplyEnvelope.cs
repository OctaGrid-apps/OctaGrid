using System.Text.Json.Serialization;

namespace OctaGrid.Protocol.AgentLink;

/// <summary>An agent's answer to one request. Exactly one of <paramref name="Reply"/> and <paramref name="Error"/> is set.</summary>
/// <param name="CorrelationId">The value from the <see cref="AgentRequestEnvelope"/> this answers.</param>
/// <param name="Reply">The reply, when the request succeeded.</param>
/// <param name="Error">The error, when it failed.</param>
public sealed record AgentReplyEnvelope(Guid CorrelationId, AgentReply? Reply, AgentError? Error = null)
{
    /// <summary>True when the request succeeded.</summary>
    [JsonIgnore]
    public bool Succeeded => Error is null;
}
