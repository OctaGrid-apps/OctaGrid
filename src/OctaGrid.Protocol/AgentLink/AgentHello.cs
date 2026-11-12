namespace OctaGrid.Protocol.AgentLink;

/// <summary>The first message of an agent that connects: who it is and which protocol it speaks.</summary>
/// <param name="AgentId">The agent's id, as it was paired.</param>
/// <param name="AgentVersion">The agent's application version, shown to the user.</param>
/// <param name="ProtocolVersion">The <see cref="AgentProtocol.Version"/> the agent was built with.</param>
public sealed record AgentHello(Guid AgentId, string AgentVersion, int ProtocolVersion);
