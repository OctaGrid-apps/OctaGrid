using OctaGrid.Application.Agents;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Local;
using OctaGrid.Extensions.Models;
using OctaGrid.Protocol.AgentLink;
using OctaGrid.Protocol.AgentLink.Notifications;
using OctaGrid.Protocol.Agents;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Desktop.Tests;

public sealed class LocalAgentNotificationsTests
{
    [Fact]
    public async Task A_notification_the_client_cannot_handle_does_not_stop_the_ones_after_it()
    {
        var link = new FakeLink();
        var handler = new FailingFirstHandler();
        var notifications = new LocalAgentNotifications(link, handler);
        using var listening = notifications.Start();
        using var stopping = new CancellationTokenSource();
        var running = notifications.RunAsync(listening, stopping.Token);

        var agentId = Guid.NewGuid();
        link.Publish(new AgentNotificationEnvelope(agentId, new ConnectorsChanged([])));
        link.Publish(new AgentNotificationEnvelope(agentId, new ConnectorsChanged([])));
        await handler.Second.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.False(running.IsCompleted);
        await stopping.CancelAsync();
        await running;
    }

    private sealed class FakeLink : IAgentLink
    {
        private readonly AgentNotificationFanOut _fanOut = new();

        public AgentConnectionState GetConnectionState(Guid agentId) => AgentConnectionState.Online;

        public Task<TReply> SendAsync<TReply>(Guid agentId, AgentRequest<TReply> request, CancellationToken cancellationToken)
            where TReply : AgentReply => throw new NotSupportedException();

        public AgentNotificationSubscription Subscribe(AgentNotificationChannel channel) => _fanOut.Subscribe(channel);

        public void Publish(AgentNotificationEnvelope envelope) => _fanOut.Publish(envelope);
    }

    /// <summary>Throws on the first connector change and signals <see cref="Second"/> on the next one.</summary>
    private sealed class FailingFirstHandler : IClientMessageHandler
    {
        private int _changes;

        public TaskCompletionSource Second { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Handle(AgentConnectorsChanged changed)
        {
            if (++_changes == 1)
            {
                throw new InvalidOperationException("The first change cannot be handled.");
            }

            Second.TrySetResult();
        }

        public void Handle(EntityChanged entity)
        {
        }

        public void Handle(PrinterTelemetry telemetryMessage, TelemetrySample? sample)
        {
        }

        public void Handle(PrinterEventRecorded recorded)
        {
        }

        public void Handle(PrinterConsoleLineRecorded recorded)
        {
        }
    }
}
