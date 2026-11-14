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
    private readonly LocalAgent _localAgent;

    public InProcessAgentLink(AgentRequestDispatcher dispatcher, AgentNotificationHub hub, LocalAgent localAgent)
    {
        _dispatcher = dispatcher;
        _hub = hub;
        _localAgent = localAgent;
    }

    public async Task<TReply> SendAsync<TReply>(Guid agentId, AgentRequest<TReply> request, CancellationToken cancellationToken)
        where TReply : AgentReply
    {
        ArgumentNullException.ThrowIfNull(request);

        // Only the local agent runs in this process; a printer assigned to any other agent cannot be reached from here.
        if (agentId != _localAgent.Id)
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
        return new Subscription(_hub, _localAgent, handler);
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Action<AgentNotificationEnvelope> _handler;
        private readonly AgentNotificationHub _hub;
        private readonly LocalAgent _localAgent;

        public Subscription(AgentNotificationHub hub, LocalAgent localAgent, Action<AgentNotificationEnvelope> handler)
        {
            _hub = hub;
            _localAgent = localAgent;
            _handler = handler;
            _hub.Published += Forward;
        }

        public void Dispose() => _hub.Published -= Forward;

        private void Forward(AgentNotification notification) =>
            // Startup provisions the id before the agent gets any printers; Guid.Empty only marks a report from before that.
            _handler(new AgentNotificationEnvelope(_localAgent.KnownId ?? Guid.Empty, notification));
    }
}
