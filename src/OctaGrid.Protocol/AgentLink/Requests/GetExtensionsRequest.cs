using OctaGrid.Protocol.AgentLink.Replies;

namespace OctaGrid.Protocol.AgentLink.Requests;

/// <summary>Asks which extensions the agent has and what state each is in.</summary>
public sealed record GetExtensionsRequest : AgentRequest<ExtensionsReply>;
