namespace OctaGrid.Protocol.AgentLink;

/// <summary>The backend's verdict on an <see cref="AgentHello"/>.</summary>
public enum AgentHelloStatus
{
    /// <summary>The agent can work with this backend.</summary>
    Accepted,

    /// <summary>The agent is too old for this backend and has to be updated first.</summary>
    AgentUpdateRequired,

    /// <summary>The agent is newer than this backend understands; the backend has to be updated first.</summary>
    BackendUpdateRequired,
}
