using OctaGrid.Extensions.Models.Exceptions;
using OctaGrid.Protocol.AgentLink;

namespace OctaGrid.Application.Agents;

/// <summary>
/// The backend's way to reach agents. Everything the backend asks of an agent goes through here as a
/// message, so the same code works whether the agent runs in this process (Local mode) or connects over
/// the network (Remote mode).
/// </summary>
public interface IAgentLink
{
    /// <summary>Sends a request to an agent and waits for its reply.</summary>
    /// <typeparam name="TReply">The reply the request is answered with.</typeparam>
    /// <param name="agentId">The agent that has to handle the request.</param>
    /// <param name="request">What is asked.</param>
    /// <param name="cancellationToken">Cancels waiting for the reply.</param>
    /// <exception cref="AppException">
    /// The agent refused the request; the exception is the error the agent reported.
    /// </exception>
    /// <exception cref="NotFoundException">No agent with that id is connected.</exception>
    Task<TReply> SendAsync<TReply>(Guid agentId, AgentRequest<TReply> request, CancellationToken cancellationToken)
        where TReply : AgentReply;

    /// <summary>
    /// Listens to what agents report on their own. The handler may be called from any thread and must not
    /// block: it runs on the thread that reported.
    /// </summary>
    /// <returns>Dispose it to stop listening.</returns>
    IDisposable Subscribe(Action<AgentNotificationEnvelope> handler);
}
