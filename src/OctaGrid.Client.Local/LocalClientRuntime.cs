using OctaGrid.Agent.Core;
using OctaGrid.Application.Agents;
using OctaGrid.Application.Events;
using OctaGrid.Application.Services;
using OctaGrid.Client.Contracts;

namespace OctaGrid.Client.Local;

/// <summary>
/// Runs Local mode's background work for the app's lifetime: it listens to the in-process agent, sends it
/// its printers and keeps them current, records print jobs from the states it reports and delivers the saved
/// domain events. Everything goes over the <see cref="IAgentLink"/>; the agent itself
/// is only held here to stop it when the app closes.
/// </summary>
internal sealed class LocalClientRuntime : IClientRuntime
{
    private readonly AgentWorker _agent;
    private readonly LocalAgentNotifications _notifications;
    private readonly OutboxDispatcher _outbox;
    private readonly PrinterStateRecorder _printerStates;
    private readonly LocalCalls _calls;
    private readonly LocalAgent _localAgent;

    public LocalClientRuntime(
        AgentWorker agent,
        LocalAgentNotifications notifications,
        LocalCalls calls,
        LocalAgent localAgent,
        PrinterStateRecorder printerStates,
        OutboxDispatcher outbox)
    {
        _agent = agent;
        _notifications = notifications;
        _calls = calls;
        _localAgent = localAgent;
        _printerStates = printerStates;
        _outbox = outbox;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Listening starts before the agent gets its printers, so nothing reported while they connect is missed.
        using var listening = _notifications.Start();
        using var recording = _printerStates.Start();

        // The backend's own background work. It runs for the app's lifetime and is stopped with the rest however
        // startup ends. Printer changes reach the agent from the outbox (AgentProfilesPublisher).
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var background = Task.WhenAll(
            _notifications.RunAsync(listening, stopping.Token),
            _outbox.RunAsync(stopping.Token),
            _printerStates.RunAsync(stopping.Token));
        try
        {
            // What a server does when an agent connects: the in-process agent is connected from the start.
            var agentId = _localAgent.Id;
            await _calls.RunAsBackend<AgentProfilesService>(profiles => profiles.Push(agentId, cancellationToken));
            await background;
        }
        finally
        {
            await stopping.CancelAsync();
            await background;
            await _agent.DisposeAsync();
        }
    }
}
