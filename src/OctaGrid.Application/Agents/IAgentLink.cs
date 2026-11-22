using OctaGrid.Extensions.Models.Exceptions;
using OctaGrid.Protocol.AgentLink;
using OctaGrid.Protocol.Agents;

namespace OctaGrid.Application.Agents;

/// <summary>
/// The backend's way to reach agents. Everything the backend asks of an agent goes through here as a
/// message, so the same code works whether the agent runs in this process (Local mode) or connects over
/// the network (Remote mode). An agent becomes reachable only after <see cref="AgentHandshake"/> accepted
/// its <see cref="AgentHello"/>.
/// </summary>
public interface IAgentLink
{
    /// <summary>Whether the agent can be reached right now, and if not, why.</summary>
    /// <param name="agentId">The agent.</param>
    AgentConnectionState GetConnectionState(Guid agentId);

    /// <summary>Sends a request to an agent and waits for its reply.</summary>
    /// <typeparam name="TReply">The reply the request is answered with.</typeparam>
    /// <param name="agentId">The agent that has to handle the request.</param>
    /// <param name="request">What is asked.</param>
    /// <param name="cancellationToken">Cancels waiting for the reply.</param>
    /// <exception cref="AppException">
    /// The agent refused the request; the exception is the error the agent reported.
    /// </exception>
    /// <exception cref="AgentUnavailableException">
    /// The agent is not connected, or its handshake was not accepted (see <see cref="GetConnectionState"/>).
    /// </exception>
    Task<TReply> SendAsync<TReply>(Guid agentId, AgentRequest<TReply> request, CancellationToken cancellationToken)
        where TReply : AgentReply;

    /// <summary>
    /// Listens to what agents report on their own on one <paramref name="channel"/>. The link writes to the
    /// subscription without waiting for the reader, so a slow reader never holds up an agent.
    /// </summary>
    /// <returns>Read its <see cref="AgentNotificationSubscription.Reader"/>; dispose it to stop listening.</returns>
    AgentNotificationSubscription Subscribe(AgentNotificationChannel channel);
}
