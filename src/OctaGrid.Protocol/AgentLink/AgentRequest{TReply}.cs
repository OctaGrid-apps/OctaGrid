namespace OctaGrid.Protocol.AgentLink;

/// <summary>A request that is answered with <typeparamref name="TReply"/>.</summary>
/// <typeparam name="TReply">The reply the agent sends when the request succeeds.</typeparam>
public abstract record AgentRequest<TReply> : AgentRequest
    where TReply : AgentReply;
