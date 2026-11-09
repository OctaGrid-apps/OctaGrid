using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Extensions.Abstractions;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Agent.Core.Events;

/// <summary>
/// Sits between the connectors and the host's own sink: holds each snapshot to the <see cref="PrinterEventLog"/>'s
/// limits, records what changed in it, then passes it on.
/// </summary>
public sealed class EventRecordingTelemetrySink : ITelemetrySink
{
    private readonly ConcurrentDictionary<Guid, byte> _failedPrinters = new();
    private readonly ITelemetrySink _inner;
    private readonly ILogger _logger;
    private readonly PrinterEventLog _log;

    public EventRecordingTelemetrySink(ITelemetrySink inner, PrinterEventLog log, ILogger<EventRecordingTelemetrySink>? logger = null)
    {
        _inner = inner;
        _log = log;
        _logger = logger ?? NullLogger<EventRecordingTelemetrySink>.Instance;
    }

    /// <inheritdoc />
    public void Send(PrinterTelemetry telemetry)
    {
        // The history is a side feature: if limiting or recording fails, the live state must still reach the clients,
        // and the connector that called Send must not be taken down by the host's own bookkeeping.
        var forward = telemetry;
        try
        {
            // Limited once, so the events recorded and the state sent on are made from the same conditions. Recorded
            // before it is passed on, so a client that reacts to the snapshot can already fetch the matching event.
            forward = _log.Sanitize(telemetry);
            _log.Observe(forward);

            // Recording works again, so the next failure of this printer is a new one worth a stack trace.
            if (!_failedPrinters.IsEmpty)
            {
                _failedPrinters.TryRemove(telemetry.PrinterId, out _);
            }
        }
        catch (Exception ex)
        {
            // A fault that keeps happening would write this for every snapshot of every printer, so the stack trace
            // is logged the first time for a printer and the repeats only at debug level.
            if (_failedPrinters.TryAdd(telemetry.PrinterId, 0))
            {
                _logger.LogError(ex, "Could not record the events of printer {PrinterId}; repeats are logged at debug level", telemetry.PrinterId);
            }
            else
            {
                _logger.LogDebug(ex, "Could not record the events of printer {PrinterId}", telemetry.PrinterId);
            }
        }

        _inner.Send(forward);
    }
}
