namespace OctaGrid.Protocol.AgentLink;

/// <summary>
/// The messages exchanged between the backend (the server, or the client in Local mode) and an agent.
/// They are plain records with no transport in them: Local mode passes them in process, Remote mode
/// sends them over SignalR.
/// </summary>
public static class AgentProtocol
{
    /// <summary>
    /// The version of this message set. Both sides send theirs in the handshake (<see cref="AgentHello"/>),
    /// so an agent and a backend that were updated separately can tell whether they understand each other.
    /// Raise it only for a change the other side cannot ignore.
    /// </summary>
    public const int Version = 1;
}
