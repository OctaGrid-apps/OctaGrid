using OctaGrid.Extensions.Models;

namespace OctaGrid.Protocol.AgentLink.Replies;

/// <summary>What starting a file involves.</summary>
/// <param name="Plan">The plates and start options, or null when the printer's connector offers no choices.</param>
public sealed record PrintStartPlanReply(PrintStartPlan? Plan) : AgentReply;
