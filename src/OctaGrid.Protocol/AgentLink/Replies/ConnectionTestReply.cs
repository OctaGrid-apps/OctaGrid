using OctaGrid.Extensions.Models;

namespace OctaGrid.Protocol.AgentLink.Replies;

/// <summary>The outcome of a connection test.</summary>
/// <param name="Result">Whether the printer could be reached, and why not.</param>
public sealed record ConnectionTestReply(ConnectionTestResult Result) : AgentReply;
