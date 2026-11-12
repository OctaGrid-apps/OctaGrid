namespace OctaGrid.Protocol.AgentLink.Replies;

/// <summary>The folder an agent loads extensions from in developer mode.</summary>
/// <param name="Folder">The folder on the agent's machine, or null when developer mode is off.</param>
public sealed record DeveloperFolderReply(string? Folder) : AgentReply;
