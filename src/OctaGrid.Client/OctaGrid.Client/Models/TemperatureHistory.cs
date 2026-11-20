using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Models;

/// <summary>One reading of the hotend and bed, with the targets that were set at that moment.</summary>
public readonly record struct TemperatureSample(
    DateTimeOffset Time,
    double Nozzle,
    double NozzleTarget,
    double Bed,
    double BedTarget);

/// <summary>
/// The client's copy of a printer's recent temperatures, for the chart on its page. The agent decides what is stored and
/// when (<see cref="TelemetrySample"/>, on the agent's clock); this copy is filled from the history the agent sends when
/// the printer starts being watched and from the samples that come with live telemetry, and keeps the last
/// <see cref="Capacity"/> of them in time order.
/// </summary>
public sealed class TemperatureHistory
{
    /// <summary>How many samples are kept: the agent's history, 20 minutes at one sample per second.</summary>
    public const int Capacity = 1200;

    private readonly Lock _gate = new();
    private readonly List<TemperatureSample> _samples = [];
    private readonly TimeProvider _timeProvider;

    public TemperatureHistory(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Raised after samples were added. It may be raised on any thread.</summary>
    public event EventHandler? Changed;

    /// <summary>The span of time the kept samples can cover.</summary>
    public static TimeSpan Window { get; } = TimeSpan.FromMinutes(20);

    /// <summary>The current time, which the chart holds the latest sample until.</summary>
    public DateTimeOffset Now => _timeProvider.GetUtcNow();

    /// <summary>
    /// Adds a sample from live telemetry. One that is not later than the newest kept sample is already here (it came with
    /// the history) and is dropped.
    /// </summary>
    /// <returns>Whether it was added.</returns>
    public bool Add(TelemetrySample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);

        lock (_gate)
        {
            if (_samples.Count > 0 && sample.Time <= _samples[^1].Time)
            {
                return false;
            }

            _samples.Add(ToTemperatures(sample));
            Trim();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Adds the history the agent sent. Live samples may already have arrived for part of it, so the two are merged by
    /// time and a time that is present twice is kept once.
    /// </summary>
    public void Merge(IReadOnlyList<TelemetrySample> history)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (history.Count == 0)
        {
            return;
        }

        lock (_gate)
        {
            var merged = _samples
                .Concat(history.Select(ToTemperatures))
                .DistinctBy(s => s.Time)
                .OrderBy(s => s.Time)
                .ToList();
            _samples.Clear();
            _samples.AddRange(merged);
            Trim();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The time of the newest kept sample, or null while there is none: where the history the agent sends can carry on from.</summary>
    public DateTimeOffset? LastTime
    {
        get
        {
            lock (_gate)
            {
                return _samples.Count > 0 ? _samples[^1].Time : null;
            }
        }
    }

    /// <summary>A copy of the kept samples, oldest first.</summary>
    public IReadOnlyList<TemperatureSample> Snapshot()
    {
        lock (_gate)
        {
            return _samples.ToArray();
        }
    }

    // The chart draws the hotend and bed; a sensor the sample does not carry is drawn at zero, as before it reported.
    private static TemperatureSample ToTemperatures(TelemetrySample sample)
    {
        var nozzle = sample.Readings.FirstOrDefault(r => r.Key == TelemetryReadingKeys.Nozzle);
        var bed = sample.Readings.FirstOrDefault(r => r.Key == TelemetryReadingKeys.Bed);
        return new TemperatureSample(
            sample.Time,
            nozzle?.Value ?? 0,
            nozzle?.Target ?? 0,
            bed?.Value ?? 0,
            bed?.Target ?? 0);
    }

    // Callers hold the lock.
    private void Trim()
    {
        if (_samples.Count > Capacity)
        {
            _samples.RemoveRange(0, _samples.Count - Capacity);
        }
    }
}
