using OctaGrid.Extensions.Models;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Agent.Core.Events;

/// <summary>
/// Remembers each printer's last snapshot and recent readings, in a fixed-size history per printer, so a client that opens
/// a printer later can show its state and draw what it missed. Every printer the agent runs is recorded, whether or not a
/// client is watching it.
/// <para>
/// Connectors may report only what changed, so each snapshot is merged into the printer's last known values first and
/// every stored sample is complete. The history lives in memory and starts empty when the agent starts.
/// </para>
/// </summary>
public sealed class PrinterTelemetryLog
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Journal> _journals = [];
    private readonly TimeProvider _timeProvider;

    public PrinterTelemetryLog(TimeProvider? timeProvider = null, PrinterTelemetryLogOptions? options = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        Options = options ?? new PrinterTelemetryLogOptions();
    }

    /// <summary>The limits this log enforces.</summary>
    public PrinterTelemetryLogOptions Options { get; }

    /// <summary>
    /// Takes in a snapshot and returns the sample stored from it, or null when none was stored: the snapshot carried no
    /// readings, or came less than <see cref="PrinterTelemetryLogOptions.MinInterval"/> after the previous sample. A
    /// snapshot of a printer that is offline or still connecting forgets the known values, so a reconnected printer
    /// never shows readings from before it went away.
    /// </summary>
    public TelemetrySample? Record(PrinterTelemetry telemetry)
    {
        ArgumentNullException.ThrowIfNull(telemetry);

        lock (_gate)
        {
            var journal = GetJournal(telemetry.PrinterId);
            journal.Latest = telemetry;
            if (telemetry.State is UniversalPrinterState.Offline or UniversalPrinterState.Connecting)
            {
                journal.Known = default;
            }

            if (telemetry is { NozzleTemperature: null, NozzleTargetTemperature: null, BedTemperature: null, BedTargetTemperature: null })
            {
                return null;
            }

            journal.Known = new KnownValues(
                telemetry.NozzleTemperature ?? journal.Known.Nozzle,
                telemetry.NozzleTargetTemperature ?? journal.Known.NozzleTarget,
                telemetry.BedTemperature ?? journal.Known.Bed,
                telemetry.BedTargetTemperature ?? journal.Known.BedTarget);

            var now = _timeProvider.GetUtcNow();
            // A clock that stepped backwards stores at once: waiting for the old time to come round again would leave a
            // hole as long as the step.
            if (journal.Samples.Count > 0
                && now >= journal.LastTime
                && now - journal.LastTime < Options.MinInterval)
            {
                return null;
            }

            var readings = journal.Known.ToReadings();
            if (readings.Count == 0)
            {
                // Only targets are known so far; a reading needs a measured value.
                return null;
            }

            var sample = new TelemetrySample(now, readings);
            journal.Samples.Enqueue(sample);
            journal.LastTime = now;
            while (journal.Samples.Count > Options.Capacity)
            {
                journal.Samples.Dequeue();
            }

            return sample;
        }
    }

    /// <summary>
    /// A printer's last snapshot and stored samples, oldest first, taken together so the two match. A printer that has
    /// reported nothing is offline with no history.
    /// </summary>
    /// <param name="printerId">The printer.</param>
    /// <param name="after">
    /// The time of the last sample the caller has, to get only what came after it; null returns everything kept.
    /// </param>
    public PrinterBackfill Get(Guid printerId, DateTimeOffset? after)
    {
        lock (_gate)
        {
            if (!_journals.TryGetValue(printerId, out var journal))
            {
                return new PrinterBackfill(printerId, new PrinterTelemetry(printerId), []);
            }

            var samples = after is { } cursor
                ? journal.Samples.Where(s => s.Time > cursor).ToList()
                : journal.Samples.ToList();
            return new PrinterBackfill(printerId, journal.Latest ?? new PrinterTelemetry(printerId), samples);
        }
    }

    /// <summary>The file of the print job the printer last reported, or null when it reported none.</summary>
    public string? CurrentFileName(Guid printerId)
    {
        lock (_gate)
        {
            return _journals.TryGetValue(printerId, out var journal) ? journal.Latest?.PrintStatus.FileName : null;
        }
    }

    /// <summary>Forgets the history of every printer that is not in <paramref name="printerIds"/>.</summary>
    public void Retain(IReadOnlyCollection<Guid> printerIds)
    {
        var keep = printerIds.ToHashSet();
        lock (_gate)
        {
            foreach (var id in _journals.Keys.Where(id => !keep.Contains(id)).ToList())
            {
                _journals.Remove(id);
            }
        }
    }

    // Callers hold the lock.
    private Journal GetJournal(Guid printerId)
    {
        if (!_journals.TryGetValue(printerId, out var journal))
        {
            journal = new Journal();
            _journals[printerId] = journal;
        }

        return journal;
    }

    private readonly record struct KnownValues(double? Nozzle, double? NozzleTarget, double? Bed, double? BedTarget)
    {
        public IReadOnlyList<TelemetryReading> ToReadings()
        {
            var readings = new List<TelemetryReading>(2);
            if (Nozzle is { } nozzle)
            {
                readings.Add(new TelemetryReading(TelemetryReadingKeys.Nozzle, nozzle, NozzleTarget));
            }

            if (Bed is { } bed)
            {
                readings.Add(new TelemetryReading(TelemetryReadingKeys.Bed, bed, BedTarget));
            }

            return readings;
        }
    }

    private sealed class Journal
    {
        public KnownValues Known { get; set; }

        public DateTimeOffset LastTime { get; set; }

        public PrinterTelemetry? Latest { get; set; }

        public Queue<TelemetrySample> Samples { get; } = new();
    }
}
