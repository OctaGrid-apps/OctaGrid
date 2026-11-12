using OctaGrid.Protocol.AgentLink.Replies;

namespace OctaGrid.Protocol.AgentLink.Requests;

/// <summary>Trusts an installed extension and turns it on.</summary>
/// <param name="ExtensionId">The extension.</param>
public sealed record ApproveExtensionRequest(string ExtensionId) : AgentRequest<ExtensionChangeReply>;
