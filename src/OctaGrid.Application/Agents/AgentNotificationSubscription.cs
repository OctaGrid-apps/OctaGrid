using System.Threading.Channels;
using OctaGrid.Protocol.AgentLink;
using OctaGrid.Protocol.AgentLink.Notifications;

namespace OctaGrid.Application.Agents;

/// <summary>
/// One listener's queue of agent notifications on one <see cref="AgentNotificationChannel"/>. The link writes
/// without waiting and the listener reads at its own pace. Events are all kept, in order. Telemetry keeps only the
/// latest snapshot of each printer while the reader is behind, because a newer snapshot replaces an older one.
/// </summary>
public sealed class AgentNotificationSubscription : IDisposable
{
    private readonly Channel<AgentNotificationEnvelope>? _events;
    private readonly Action<AgentNotificationSubscription>? _onDispose;
    private readonly LatestTelemetry? _telemetry;

    /// <param name="channel">The stream this subscription receives.</param>
    /// <param name="onDispose">Called once when the subscription is disposed, so the link stops writing to it.</param>
    public AgentNotificationSubscription(AgentNotificationChannel channel, Action<AgentNotificationSubscription>? onDispose = null)
    {
        Channel = channel;
        _onDispose = onDispose;
        if (channel == AgentNotificationChannel.Telemetry)
        {
            _telemetry = new LatestTelemetry();
        }
        else
        {
            _events = System.Threading.Channels.Channel.CreateUnbounded<AgentNotificationEnvelope>(
                new UnboundedChannelOptions { SingleReader = true });
        }
    }

    /// <summary>The stream this subscription receives.</summary>
    public AgentNotificationChannel Channel { get; }

    /// <summary>What arrived, oldest first. Completes when the subscription is disposed.</summary>
    public ChannelReader<AgentNotificationEnvelope> Reader => _telemetry ?? _events!.Reader;

    /// <summary>Queues a notification without waiting. Called by links.</summary>
    /// <returns>False when the subscription was disposed.</returns>
    public bool TryWrite(AgentNotificationEnvelope envelope) =>
        _telemetry?.TryWrite(envelope) ?? _events!.Writer.TryWrite(envelope);

    public void Dispose()
    {
        var completed = _telemetry?.TryComplete() ?? _events!.Writer.TryComplete();
        if (completed)
        {
            _onDispose?.Invoke(this);
        }
    }

    /// <summary>
    /// Telemetry waiting for the reader: at most one snapshot per printer, read in the order the printers first
    /// reported. A snapshot without a history sample keeps the sample of the one it replaces.
    /// </summary>
    private sealed class LatestTelemetry : ChannelReader<AgentNotificationEnvelope>
    {
        private readonly Dictionary<(Guid AgentId, Guid Key), AgentNotificationEnvelope> _latest = [];
        private readonly Lock _lock = new();
        private readonly Channel<(Guid AgentId, Guid Key)> _order =
            System.Threading.Channels.Channel.CreateUnbounded<(Guid, Guid)>(new UnboundedChannelOptions { SingleReader = true });

        private bool _completed;

        public override bool CanCount => true;

        public override Task Completion => _order.Reader.Completion;

        public override int Count
        {
            get
            {
                lock (_lock)
                {
                    return _latest.Count;
                }
            }
        }

        public bool TryWrite(AgentNotificationEnvelope envelope)
        {
            // Telemetry of another kind is never replaced, so it gets a key of its own.
            var key = (envelope.AgentId, envelope.Notification is PrinterTelemetryReported reported
                ? reported.Telemetry.PrinterId
                : Guid.NewGuid());

            lock (_lock)
            {
                if (_completed)
                {
                    return false;
                }

                if (_latest.TryGetValue(key, out var waiting))
                {
                    _latest[key] = Replace(waiting, envelope);
                    return true;
                }

                _latest[key] = envelope;
                return _order.Writer.TryWrite(key);
            }
        }

        public bool TryComplete()
        {
            lock (_lock)
            {
                if (_completed)
                {
                    return false;
                }

                _completed = true;
            }

            return _order.Writer.TryComplete();
        }

        public override bool TryRead(out AgentNotificationEnvelope item)
        {
            while (_order.Reader.TryRead(out var key))
            {
                lock (_lock)
                {
                    if (_latest.Remove(key, out item!))
                    {
                        return true;
                    }
                }
            }

            item = null!;
            return false;
        }

        public override ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default) =>
            _order.Reader.WaitToReadAsync(cancellationToken);

        private static AgentNotificationEnvelope Replace(AgentNotificationEnvelope waiting, AgentNotificationEnvelope newer) =>
            waiting.Notification is PrinterTelemetryReported { Sample: { } sample }
            && newer.Notification is PrinterTelemetryReported { Sample: null } reported
                ? newer with { Notification = reported with { Sample = sample } }
                : newer;
    }
}
