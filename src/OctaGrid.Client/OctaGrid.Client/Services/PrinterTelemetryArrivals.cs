using System.Collections.Concurrent;

namespace OctaGrid.Client.Services;

/// <summary>
/// Counts the telemetry that arrived for each printer. A state the client asked for is older than any telemetry that
/// arrived while it waited for the answer, so <see cref="PrinterWatcher"/> compares the counts from before and after
/// and drops the answer's state when live telemetry got there first. Thread-safe.
/// </summary>
public sealed class PrinterTelemetryArrivals
{
    private readonly ConcurrentDictionary<Guid, long> _counts = new();

    /// <summary>Counts one arrival.</summary>
    public void Record(Guid printerId) => _counts.AddOrUpdate(printerId, 1, (_, count) => count + 1);

    /// <summary>How many arrived so far.</summary>
    public long Count(Guid printerId) => _counts.GetValueOrDefault(printerId);
}
