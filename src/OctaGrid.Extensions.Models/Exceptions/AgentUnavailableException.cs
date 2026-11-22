namespace OctaGrid.Extensions.Models.Exceptions;

/// <summary>
/// The agent a request has to go to cannot be reached: it is not connected, or it connected with a version the
/// backend does not accept. Trying again after the agent is back (or updated) usually succeeds. A server maps it
/// to <c>503 Service Unavailable</c>.
/// </summary>
public sealed class AgentUnavailableException : AppException
{
    /// <summary>The problem type URI of an unavailable agent.</summary>
    public const string Uri = "/errors/agent-unavailable";

    /// <summary>Creates the error for an agent, with what the user can do about it.</summary>
    /// <param name="agentId">The agent that cannot be reached.</param>
    /// <param name="detail">Why, written for the user.</param>
    public AgentUnavailableException(Guid agentId, string detail)
        : base(detail)
    {
        AgentId = agentId;
    }

    /// <summary>The agent that cannot be reached.</summary>
    public Guid AgentId { get; }

    /// <inheritdoc />
    public override string TypeUri => Uri;

    /// <inheritdoc />
    public override string Title => "Agent Unavailable";
}
