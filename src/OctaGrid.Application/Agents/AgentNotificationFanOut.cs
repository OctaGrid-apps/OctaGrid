using OctaGrid.Protocol.AgentLink;

namespace OctaGrid.Application.Agents;

/// <summary>
/// Hands each agent notification to every subscription of its channel. Links use it to implement
/// <see cref="IAgentLink.Subscribe"/>. It is thread-safe and never blocks the thread that reports.
/// </summary>
public sealed class AgentNotificationFanOut
{
    private readonly Lock _lock = new();
    private AgentNotificationSubscription[] _subscriptions = [];

    /// <summary>Adds a listener on <paramref name="channel"/>.</summary>
    public AgentNotificationSubscription Subscribe(AgentNotificationChannel channel)
    {
        var subscription = new AgentNotificationSubscription(channel, Remove);
        lock (_lock)
        {
            _subscriptions = [.. _subscriptions, subscription];
        }

        return subscription;
    }

    /// <summary>Queues a notification for every listener on its channel.</summary>
    public void Publish(AgentNotificationEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var channel = envelope.Notification.Channel;
        // Read without the lock: the array is replaced as a whole and never changed.
        foreach (var subscription in Volatile.Read(ref _subscriptions))
        {
            if (subscription.Channel == channel)
            {
                subscription.TryWrite(envelope);
            }
        }
    }

    private void Remove(AgentNotificationSubscription subscription)
    {
        lock (_lock)
        {
            _subscriptions = _subscriptions.Where(s => s != subscription).ToArray();
        }
    }
}
