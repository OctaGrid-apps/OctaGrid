using OctaGrid.Protocol.AgentLink.Replies;

namespace OctaGrid.Protocol.AgentLink.Requests;

/// <summary>Turns an extension on or off.</summary>
/// <param name="ExtensionId">The extension.</param>
/// <param name="Enabled">True to turn it on.</param>
public sealed record SetExtensionEnabledRequest(string ExtensionId, bool Enabled) : AgentRequest<AgentAck>;
