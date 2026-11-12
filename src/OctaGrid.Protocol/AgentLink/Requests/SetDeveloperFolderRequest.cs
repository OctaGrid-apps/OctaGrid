using OctaGrid.Protocol.AgentLink.Replies;

namespace OctaGrid.Protocol.AgentLink.Requests;

/// <summary>Turns developer mode on for a folder, or off.</summary>
/// <param name="Folder">A folder on the agent's machine, or null to turn developer mode off.</param>
public sealed record SetDeveloperFolderRequest(string? Folder) : AgentRequest<ExtensionChangeReply>;
