using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Application.Agents;
using OctaGrid.Application.Services;
using OctaGrid.Protocol.AgentLink;
using OctaGrid.Protocol.AgentLink.Notifications;

namespace OctaGrid.Application.Events;

/// <summary>
/// The backend's consumer of the state changes agents report. Queues them as they arrive, because the link's
/// handler must not block, and records them one at a time, in order, through <see cref="PrinterStateService"/>.
/// </summary>
public sealed class PrinterStateRecorder
{
    private readonly Channel<(Guid AgentId, PrinterStateChanged Change)> _queue =
        Channel.CreateUnbounded<(Guid, PrinterStateChanged)>(new UnboundedChannelOptions { SingleReader = true });

    private readonly IAgentLink _link;
    private readonly ILogger<PrinterStateRecorder> _logger;
    private readonly IServiceScopeFactory _scopes;

    public PrinterStateRecorder(IAgentLink link, IServiceScopeFactory scopes, ILogger<PrinterStateRecorder>? logger = null)
    {
        _link = link;
        _scopes = scopes;
        _logger = logger ?? NullLogger<PrinterStateRecorder>.Instance;
    }

    /// <summary>Starts listening to the agents.</summary>
    /// <returns>Dispose it to stop.</returns>
    public IDisposable Start() => _link.Subscribe(OnNotification);

    /// <summary>Records what arrives until cancelled.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync(cancellationToken))
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
        while (_queue.Reader.TryRead(out var item))
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<PrinterStateService>()
                    .RecordStateChange(item.AgentId, item.Change, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // One change that cannot be recorded must not stop the ones after it.
                _logger.LogError(ex, "Could not record the state change of printer {PrinterId} to {State}",
                    item.Change.PrinterId, item.Change.Current);
            }
        }
    }

    private void OnNotification(AgentNotificationEnvelope envelope)
    {
        if (envelope.Notification is PrinterStateChanged change)
        {
            _queue.Writer.TryWrite((envelope.AgentId, change));
        }
    }
}
