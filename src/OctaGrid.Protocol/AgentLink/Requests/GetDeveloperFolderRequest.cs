using OctaGrid.Protocol.AgentLink.Replies;

namespace OctaGrid.Protocol.AgentLink.Requests;

/// <summary>Asks which folder the agent loads extensions from in developer mode.</summary>
public sealed record GetDeveloperFolderRequest : AgentRequest<DeveloperFolderReply>;
