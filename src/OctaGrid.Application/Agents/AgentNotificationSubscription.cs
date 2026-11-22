using System.Threading.Channels;
using OctaGrid.Protocol.AgentLink;

namespace OctaGrid.Application.Agents;

/// <summary>
/// One listener's queue of agent notifications on one <see cref="AgentNotificationChannel"/>. The link writes
/// without waiting and the listener reads at its own pace. Events are all kept, in order. Telemetry is bounded and
/// drops the oldest snapshot when the reader falls behind, because a newer snapshot replaces it anyway.
/// </summary>
public sealed class AgentNotificationSubscription : IDisposable
{
    /// <summary>How many telemetry snapshots wait for a reader before the oldest is dropped.</summary>
    public const int TelemetryCapacity = 1024;

    private readonly Action<AgentNotificationSubscription>? _onDispose;
    private readonly Channel<AgentNotificationEnvelope> _queue;

    /// <param name="channel">The stream this subscription receives.</param>
    /// <param name="onDispose">Called once when the subscription is disposed, so the link stops writing to it.</param>
    public AgentNotificationSubscription(AgentNotificationChannel channel, Action<AgentNotificationSubscription>? onDispose = null)
    {
        Channel = channel;
        _onDispose = onDispose;
        _queue = channel == AgentNotificationChannel.Telemetry
            ? System.Threading.Channels.Channel.CreateBounded<AgentNotificationEnvelope>(new BoundedChannelOptions(TelemetryCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            })
            : System.Threading.Channels.Channel.CreateUnbounded<AgentNotificationEnvelope>(new UnboundedChannelOptions { SingleReader = true });
    }

    /// <summary>The stream this subscription receives.</summary>
    public AgentNotificationChannel Channel { get; }

    /// <summary>What arrived, oldest first. Completes when the subscription is disposed.</summary>
    public ChannelReader<AgentNotificationEnvelope> Reader => _queue.Reader;

    /// <summary>Queues a notification without waiting. Called by links.</summary>
    /// <returns>False when the subscription was disposed.</returns>
    public bool TryWrite(AgentNotificationEnvelope envelope) => _queue.Writer.TryWrite(envelope);

    public void Dispose()
    {
        if (_queue.Writer.TryComplete())
        {
            _onDispose?.Invoke(this);
        }
    }
}
