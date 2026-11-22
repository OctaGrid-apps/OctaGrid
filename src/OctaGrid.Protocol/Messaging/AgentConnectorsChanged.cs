namespace OctaGrid.Protocol.Messaging;

/// <summary>
/// Sent to clients when the connectors an agent offers changed, for example after an extension was installed or
/// turned off, so they read the agent's connectors again.
/// </summary>
/// <param name="AgentId">The agent whose connectors changed.</param>
public record AgentConnectorsChanged(Guid AgentId);
