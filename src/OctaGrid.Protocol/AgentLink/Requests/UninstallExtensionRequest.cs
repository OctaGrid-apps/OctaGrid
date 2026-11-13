using OctaGrid.Protocol.AgentLink.Replies;

namespace OctaGrid.Protocol.AgentLink.Requests;

/// <summary>Removes an installed extension.</summary>
/// <param name="ExtensionId">The extension.</param>
public sealed record UninstallExtensionRequest(string ExtensionId) : AgentRequest<ExtensionChangeReply>;
