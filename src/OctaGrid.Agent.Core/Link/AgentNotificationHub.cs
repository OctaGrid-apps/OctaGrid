using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Protocol.AgentLink;

namespace OctaGrid.Agent.Core.Link;

/// <summary>
/// The one place everything an agent reports on its own passes through: what the <see cref="AgentRequestDispatcher"/>
/// forwards from the worker (telemetry, events, console lines, connector changes). Whoever connects the agent to a
/// backend subscribes here.
/// </summary>
public sealed class AgentNotificationHub
{
    private readonly ILogger<AgentNotificationHub> _logger;

    public AgentNotificationHub(ILogger<AgentNotificationHub>? logger = null)
    {
        _logger = logger ?? NullLogger<AgentNotificationHub>.Instance;
    }

    /// <summary>Raised for every notification, on the thread that reported it. May be raised from any thread.</summary>
    public event Action<AgentNotification>? Published;

    /// <summary>Hands a notification to the subscribers.</summary>
    public void Publish(AgentNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var subscribers = Published;
        if (subscribers is null)
        {
            return;
        }

        foreach (var subscriber in subscribers.GetInvocationList().Cast<Action<AgentNotification>>())
        {
            try
            {
                subscriber(notification);
            }
            catch (Exception ex)
            {
                // A broken listener must not break the printer session that reported this.
                _logger.LogError(ex, "A subscriber failed to handle {NotificationType}", notification.GetType().Name);
            }
        }
    }
}
