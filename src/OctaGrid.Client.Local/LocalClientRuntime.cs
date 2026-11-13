using OctaGrid.Agent.Core;
using OctaGrid.Application.Agents;
using OctaGrid.Client.Contracts;
using OctaGrid.Protocol.AgentLink.Requests;

namespace OctaGrid.Client.Local;

/// <summary>
/// Runs Local mode's background work for the app's lifetime: it listens to the in-process agent, sends it
/// its printers and keeps them current. Everything goes over the <see cref="IAgentLink"/>; the agent itself
/// is only held here to stop it when the app closes.
/// </summary>
internal sealed class LocalClientRuntime : IClientRuntime
{
    private readonly AgentWorker _agent;
    private readonly LocalConnectorCache _connectors;
    private readonly IAgentLink _link;
    private readonly LocalAgentNotifications _notifications;
    private readonly LocalAgentSynchronizer _synchronizer;

    public LocalClientRuntime(
        AgentWorker agent,
        IAgentLink link,
        LocalAgentNotifications notifications,
        LocalConnectorCache connectors,
        LocalAgentSynchronizer synchronizer)
    {
        _agent = agent;
        _link = link;
        _notifications = notifications;
        _connectors = connectors;
        _synchronizer = synchronizer;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Listening starts before the agent gets its printers, so nothing reported while they connect is missed.
        using var listening = _notifications.Start();
        try
        {
            // First, and before anything that really waits: the in-process agent answers this at once, so the
            // connector list is there before the first window asks for it.
            _connectors.Set((await _link.SendAsync(LocalAgent.Id, new GetConnectorsRequest(), cancellationToken)).Connectors);

            await _synchronizer.PushProfilesAsync(cancellationToken);
            // Runs for the app's lifetime; ends when cancellationToken is cancelled.
            await _synchronizer.RunAsync(cancellationToken);
        }
        finally
        {
            await _agent.DisposeAsync();
        }
    }
}
