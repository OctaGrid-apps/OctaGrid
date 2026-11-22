namespace OctaGrid.Protocol.Agents;

/// <summary>An agent of a workspace.</summary>
/// <param name="Id">The agent's id.</param>
/// <param name="Name">The agent's name.</param>
/// <param name="Status">Where the agent is in its lifecycle (pending, active, ...).</param>
/// <param name="Token">The visible prefix of the agent's token, or null.</param>
/// <param name="Version">The row version, for optimistic concurrency.</param>
public record AgentResponse(Guid Id, string Name, string Status, string? Token, long Version)
{
    /// <summary>Whether the backend can reach the agent right now.</summary>
    public AgentConnectionState Connection { get; init; } = AgentConnectionState.Offline;
}
