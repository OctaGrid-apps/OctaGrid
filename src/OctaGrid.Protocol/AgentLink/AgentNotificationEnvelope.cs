namespace OctaGrid.Protocol.AgentLink;

/// <summary>A notification on its way from an agent.</summary>
/// <param name="AgentId">The agent that sent it.</param>
/// <param name="Notification">What it reports.</param>
public sealed record AgentNotificationEnvelope(Guid AgentId, AgentNotification Notification);
