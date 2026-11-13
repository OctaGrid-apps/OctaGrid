using OctaGrid.Agent.Core.Link;
using OctaGrid.Application.Agents;
using OctaGrid.Extensions.Models.Exceptions;
using OctaGrid.Protocol.AgentLink;

namespace OctaGrid.Client.Local;

/// <summary>
/// The link to the agent that runs in this process. It passes the same messages a network link would, without
/// serializing them: a request goes straight to the agent's <see cref="AgentRequestDispatcher"/> and what the
/// agent reports comes back from its <see cref="AgentNotificationHub"/>.
/// </summary>
internal sealed class InProcessAgentLink : IAgentLink
{
    private readonly AgentRequestDispatcher _dispatcher;
    private readonly AgentNotificationHub _hub;

    public InProcessAgentLink(AgentRequestDispatcher dispatcher, AgentNotificationHub hub)
    {
        _dispatcher = dispatcher;
        _hub = hub;
    }

    public async Task<TReply> SendAsync<TReply>(Guid agentId, AgentRequest<TReply> request, CancellationToken cancellationToken)
        where TReply : AgentReply
    {
        ArgumentNullException.ThrowIfNull(request);

        if (agentId != LocalAgent.Id)
        {
            throw new NotFoundException("Agent", agentId);
        }

        var envelope = new AgentRequestEnvelope(agentId, Guid.NewGuid(), request);
        var answer = await _dispatcher.DispatchAsync(envelope, cancellationToken);
        if (answer.Error is { } error)
        {
            throw error.ToException();
        }

        // The dispatcher answers every request with the reply type the request names; anything else is a bug in it.
        return answer.Reply as TReply
            ?? throw new InvalidOperationException(
                $"The agent answered {request.GetType().Name} with {answer.Reply?.GetType().Name ?? "nothing"} instead of {typeof(TReply).Name}.");
    }

    public IDisposable Subscribe(Action<AgentNotificationEnvelope> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Subscription(_hub, handler);
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Action<AgentNotificationEnvelope> _handler;
        private readonly AgentNotificationHub _hub;

        public Subscription(AgentNotificationHub hub, Action<AgentNotificationEnvelope> handler)
        {
            _hub = hub;
            _handler = handler;
            _hub.Published += Forward;
        }

        public void Dispose() => _hub.Published -= Forward;

        private void Forward(AgentNotification notification) =>
            _handler(new AgentNotificationEnvelope(LocalAgent.Id, notification));
    }
}
