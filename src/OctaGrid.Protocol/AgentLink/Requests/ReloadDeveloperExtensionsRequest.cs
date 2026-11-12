using OctaGrid.Protocol.AgentLink.Replies;

namespace OctaGrid.Protocol.AgentLink.Requests;

/// <summary>Loads the developer folder again, for example right after a build.</summary>
public sealed record ReloadDeveloperExtensionsRequest : AgentRequest<ExtensionChangeReply>;
