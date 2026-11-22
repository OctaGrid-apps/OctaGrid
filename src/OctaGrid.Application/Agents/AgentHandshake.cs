using OctaGrid.Extensions.Models.Exceptions;
using OctaGrid.Protocol.AgentLink;
using OctaGrid.Protocol.Agents;

namespace OctaGrid.Application.Agents;

/// <summary>
/// The backend's answer to an agent that connects. Agent and backend are updated separately, so the agent says
/// which protocol version it speaks and the backend accepts it or says which side has to be updated. A link asks
/// this before it lets any request through to an agent.
/// </summary>
public static class AgentHandshake
{
    /// <summary>Decides whether the agent may continue.</summary>
    /// <param name="hello">What the agent sent.</param>
    /// <param name="backendVersion">The protocol version of this backend.</param>
    public static AgentHelloResult Answer(AgentHello hello, int backendVersion = AgentProtocol.Version)
    {
        ArgumentNullException.ThrowIfNull(hello);

        if (hello.ProtocolVersion < backendVersion)
        {
            return new AgentHelloResult(AgentHelloStatus.AgentUpdateRequired, backendVersion,
                $"Agent {hello.AgentVersion} is too old for this app. Update the agent.");
        }

        if (hello.ProtocolVersion > backendVersion)
        {
            return new AgentHelloResult(AgentHelloStatus.BackendUpdateRequired, backendVersion,
                $"Agent {hello.AgentVersion} is newer than this app understands. Update the app.");
        }

        return new AgentHelloResult(AgentHelloStatus.Accepted, backendVersion);
    }

    /// <summary>What a verdict means for reaching the agent.</summary>
    public static AgentConnectionState ToConnectionState(AgentHelloStatus status) => status switch
    {
        AgentHelloStatus.Accepted => AgentConnectionState.Online,
        AgentHelloStatus.AgentUpdateRequired => AgentConnectionState.AgentUpdateRequired,
        AgentHelloStatus.BackendUpdateRequired => AgentConnectionState.BackendUpdateRequired,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    /// <summary>The error for a request to an agent that cannot be reached in <paramref name="state"/>.</summary>
    public static AgentUnavailableException Unavailable(Guid agentId, AgentConnectionState state) => new(agentId, state switch
    {
        AgentConnectionState.AgentUpdateRequired => "The agent is too old for this app. Update the agent.",
        AgentConnectionState.BackendUpdateRequired => "The agent is newer than this app understands. Update the app.",
        _ => "The agent is not connected. Check that it is running and online.",
    });
}
