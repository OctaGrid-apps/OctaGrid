namespace OctaGrid.Protocol.AgentLink;

/// <summary>
/// Which of an agent's two streams a notification travels on. They are kept apart because they differ in volume
/// and in what may be lost: Remote mode sends events over SignalR and telemetry over WebRTC. New values go at the end.
/// </summary>
public enum AgentNotificationChannel
{
    /// <summary>
    /// Rare reports that must all arrive, in order: recorded events, console lines, state changes, connector changes.
    /// </summary>
    Events,

    /// <summary>
    /// Frequent printer snapshots. Each one replaces the one before, so a receiver that falls behind may keep only
    /// the latest of each printer instead of slowing the agent down.
    /// </summary>
    Telemetry,
}
