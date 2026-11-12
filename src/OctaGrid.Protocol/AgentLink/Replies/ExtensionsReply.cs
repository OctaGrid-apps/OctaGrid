using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Protocol.AgentLink.Replies;

/// <summary>The extensions an agent has.</summary>
/// <param name="Extensions">The extensions and the state each is in.</param>
public sealed record ExtensionsReply(IReadOnlyList<ExtensionInfo> Extensions) : AgentReply;
