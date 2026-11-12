namespace OctaGrid.Protocol.AgentLink;

/// <summary>The backend's answer to an <see cref="AgentHello"/>.</summary>
/// <param name="Status">Whether the agent may continue.</param>
/// <param name="ProtocolVersion">The <see cref="AgentProtocol.Version"/> the backend was built with.</param>
/// <param name="Message">What the user has to do when the agent was not accepted. Null when it was.</param>
public sealed record AgentHelloResult(AgentHelloStatus Status, int ProtocolVersion, string? Message = null);
