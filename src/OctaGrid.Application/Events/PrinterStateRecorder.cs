using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Application.Agents;
using OctaGrid.Application.Services;
using OctaGrid.Protocol.AgentLink;
using OctaGrid.Protocol.AgentLink.Notifications;

namespace OctaGrid.Application.Events;

/// <summary>
/// The backend's consumer of the state changes agents report. It listens on the events channel, which keeps every
/// change in order, and records them one at a time through <see cref="PrinterStateService"/>.
/// </summary>
public sealed class PrinterStateRecorder
{
    private readonly IAgentLink _link;
    private readonly ILogger<PrinterStateRecorder> _logger;
    private readonly IServiceScopeFactory _scopes;
    private AgentNotificationSubscription? _subscription;

    public PrinterStateRecorder(IAgentLink link, IServiceScopeFactory scopes, ILogger<PrinterStateRecorder>? logger = null)
    {
        _link = link;
        _scopes = scopes;
        _logger = logger ?? NullLogger<PrinterStateRecorder>.Instance;
    }

    /// <summary>Starts listening to the agents.</summary>
    /// <returns>Dispose it to stop.</returns>
    public IDisposable Start()
    {
        var subscription = _link.Subscribe(AgentNotificationChannel.Events);
        _subscription = subscription;
        return subscription;
    }

    /// <summary>Records what arrives until cancelled or until listening stops.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var subscription = _subscription ?? throw new InvalidOperationException("Call Start before RunAsync.");
        try
        {
            while (await subscription.Reader.WaitToReadAsync(cancellationToken))
            {
                await RecordQueuedAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>Records what has arrived so far.</summary>
    public async Task RecordQueuedAsync(CancellationToken cancellationToken)
    {
        if (_subscription is not { } subscription)
        {
            return;
        }

        while (subscription.Reader.TryRead(out var envelope))
        {
            if (envelope.Notification is not PrinterStateChanged change)
            {
                continue;
            }

            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<PrinterStateService>()
                    .RecordStateChange(envelope.AgentId, change, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // One change that cannot be recorded must not stop the ones after it.
                _logger.LogError(ex, "Could not record the state change of printer {PrinterId} to {State}",
                    change.PrinterId, change.Current);
            }
        }
    }
}
