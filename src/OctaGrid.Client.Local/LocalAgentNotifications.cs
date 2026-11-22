using OctaGrid.Application.Agents;
using OctaGrid.Client.Contracts;
using OctaGrid.Protocol.AgentLink;
using OctaGrid.Protocol.AgentLink.Notifications;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Local;

/// <summary>
/// Passes what the local agent reports on to the UI: telemetry, recorded events and console lines go to the
/// client's message handler, and a changed connector list tells the client to read the agent's connectors again.
/// Telemetry and events are read from separate channels, so a burst of telemetry never delays an event.
/// </summary>
internal sealed class LocalAgentNotifications
{
    private readonly IAgentLink _link;
    private readonly IClientMessageHandler _messageHandler;

    public LocalAgentNotifications(IAgentLink link, IClientMessageHandler messageHandler)
    {
        _link = link;
        _messageHandler = messageHandler;
    }

    /// <summary>Starts listening.</summary>
    /// <returns>The subscriptions; pass them to <see cref="RunAsync"/>, and dispose them to stop.</returns>
    public Listening Start() => new(_link.Subscribe(AgentNotificationChannel.Events), _link.Subscribe(AgentNotificationChannel.Telemetry));

    /// <summary>Hands on what arrives until cancelled or until <paramref name="listening"/> is disposed.</summary>
    public Task RunAsync(Listening listening, CancellationToken cancellationToken) =>
        Task.WhenAll(ReadAsync(listening.Events, cancellationToken), ReadAsync(listening.Telemetry, cancellationToken));

    private async Task ReadAsync(AgentNotificationSubscription subscription, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var envelope in subscription.Reader.ReadAllAsync(cancellationToken))
            {
                Handle(envelope);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void Handle(AgentNotificationEnvelope envelope)
    {
        switch (envelope.Notification)
        {
            case PrinterTelemetryReported telemetry:
                _messageHandler.Handle(telemetry.Telemetry, telemetry.Sample);
                break;

            case PrinterEventRecorded recorded:
                _messageHandler.Handle(recorded);
                break;

            case PrinterConsoleLineRecorded recorded:
                _messageHandler.Handle(recorded);
                break;

            case ConnectorsChanged:
                _messageHandler.Handle(new AgentConnectorsChanged(envelope.AgentId));
                break;
        }
    }

    /// <summary>The two subscriptions of a running listener.</summary>
    public sealed class Listening : IDisposable
    {
        public Listening(AgentNotificationSubscription events, AgentNotificationSubscription telemetry)
        {
            Events = events;
            Telemetry = telemetry;
        }

        public AgentNotificationSubscription Events { get; }

        public AgentNotificationSubscription Telemetry { get; }

        public void Dispose()
        {
            Events.Dispose();
            Telemetry.Dispose();
        }
    }
}
