namespace OctaGrid.Client.Services;

/// <summary>An agent reported that the connectors it offers changed.</summary>
/// <param name="AgentId">The agent.</param>
public sealed record AgentConnectorsChangedMessage(Guid AgentId);
