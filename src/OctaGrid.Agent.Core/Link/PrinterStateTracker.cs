using System.Collections.Concurrent;
using OctaGrid.Extensions.Models;
using OctaGrid.Protocol.AgentLink.Notifications;

namespace OctaGrid.Agent.Core.Link;

/// <summary>
/// Remembers the last <see cref="UniversalPrinterState"/> of each printer and tells when a snapshot changes it.
/// Safe to call from the connector threads that report telemetry.
/// </summary>
public sealed class PrinterStateTracker
{
    private readonly ConcurrentDictionary<Guid, UniversalPrinterState> _states = new();
    private readonly TimeProvider _time;

    public PrinterStateTracker(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Returns the change the snapshot makes, or null when the printer's state is what it was.</summary>
    public PrinterStateChanged? Observe(PrinterTelemetry telemetry)
    {
        ArgumentNullException.ThrowIfNull(telemetry);

        UniversalPrinterState? previous = null;
        var current = telemetry.State;
        var changed = false;
        _states.AddOrUpdate(
            telemetry.PrinterId,
            _ =>
            {
                previous = null;
                changed = true;
                return current;
            },
            (_, last) =>
            {
                previous = last;
                changed = last != current;
                return current;
            });

        return changed
            ? new PrinterStateChanged(telemetry.PrinterId, previous, current, telemetry.PrintStatus?.FileName, _time.GetUtcNow())
            : null;
    }

    /// <summary>Forgets the printers the agent no longer runs, so one it gets back starts without a previous state.</summary>
    public void Retain(IReadOnlyCollection<Guid> printerIds)
    {
        foreach (var printerId in _states.Keys.Except(printerIds))
        {
            _states.TryRemove(printerId, out _);
        }
    }
}
