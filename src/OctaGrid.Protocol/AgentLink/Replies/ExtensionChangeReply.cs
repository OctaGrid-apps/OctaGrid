using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Protocol.AgentLink.Replies;

/// <summary>The outcome of installing, approving, removing or reloading extensions.</summary>
/// <param name="Result">Whether it was done, and the message for the user.</param>
public sealed record ExtensionChangeReply(ExtensionInstallResult Result) : AgentReply;
