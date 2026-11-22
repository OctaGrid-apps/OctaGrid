using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Agent.Core.Link;
using OctaGrid.Application.Agents;
using OctaGrid.Protocol.AgentLink;
using OctaGrid.Protocol.Agents;

namespace OctaGrid.Client.Local;

/// <summary>
/// The link to the agent that runs in this process. It passes the same messages a network link would, without
/// serializing them: a request goes straight to the agent's <see cref="AgentRequestDispatcher"/> and what the
/// agent reports comes back from its <see cref="AgentNotificationHub"/>. Like a network link, it lets requests
/// through only after the agent's handshake was accepted; the in-process agent is there from the start, so the
/// handshake happens on first use.
/// </summary>
internal sealed class InProcessAgentLink : IAgentLink
{
    private readonly AgentRequestDispatcher _dispatcher;
    private readonly AgentNotificationFanOut _fanOut = new();
    private readonly LocalAgent _localAgent;
    private readonly ILogger<InProcessAgentLink> _logger;
    private readonly Lock _lock = new();
    private AgentConnectionState? _state;

    public InProcessAgentLink(
        AgentRequestDispatcher dispatcher,
        AgentNotificationHub hub,
        LocalAgent localAgent,
        ILogger<InProcessAgentLink>? logger = null)
    {
        _dispatcher = dispatcher;
        _localAgent = localAgent;
        _logger = logger ?? NullLogger<InProcessAgentLink>.Instance;

        // Both live for the whole process, so the link never unsubscribes.
        hub.Published += Forward;
    }

    public AgentConnectionState GetConnectionState(Guid agentId) =>
        // Only the local agent runs in this process; any other agent is never connected here.
        agentId == _localAgent.KnownId ? Connect(agentId) : AgentConnectionState.Offline;

    public async Task<TReply> SendAsync<TReply>(Guid agentId, AgentRequest<TReply> request, CancellationToken cancellationToken)
        where TReply : AgentReply
    {
        ArgumentNullException.ThrowIfNull(request);

        var state = GetConnectionState(agentId);
        if (state != AgentConnectionState.Online)
        {
            throw AgentHandshake.Unavailable(agentId, state);
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

    public AgentNotificationSubscription Subscribe(AgentNotificationChannel channel) => _fanOut.Subscribe(channel);

    // The same handshake a remote agent goes through, once. Agent and app are built together here, so it is always
    // accepted in practice; it still runs, so the verdict a remote agent gets is the one this agent gets.
    private AgentConnectionState Connect(Guid agentId)
    {
        lock (_lock)
        {
            if (_state is { } known)
            {
                return known;
            }

            var hello = AgentRequestDispatcher.Hello(agentId);
            var result = AgentHandshake.Answer(hello);
            if (result.Status == AgentHelloStatus.Accepted)
            {
                _logger.LogInformation("Local agent {AgentId} {AgentVersion} connected with protocol {ProtocolVersion}",
                    agentId, hello.AgentVersion, hello.ProtocolVersion);
            }
            else
            {
                _logger.LogWarning("Local agent {AgentId} {AgentVersion} was not accepted: {Status} (agent protocol {AgentProtocol}, app protocol {AppProtocol})",
                    agentId, hello.AgentVersion, result.Status, hello.ProtocolVersion, result.ProtocolVersion);
            }

            var state = AgentHandshake.ToConnectionState(result.Status);
            _state = state;
            return state;
        }
    }

    // Called on the agent's threads; the fan-out only queues, so the agent never waits for a listener.
    private void Forward(AgentNotification notification) =>
        // Startup provisions the id before the agent gets any printers; Guid.Empty only marks a report from before that.
        _fanOut.Publish(new AgentNotificationEnvelope(_localAgent.KnownId ?? Guid.Empty, notification));
}
