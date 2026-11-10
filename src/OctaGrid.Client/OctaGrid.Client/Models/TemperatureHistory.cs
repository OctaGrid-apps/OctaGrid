namespace OctaGrid.Client.Models;

/// <summary>One reading of the hotend and bed, with the targets that were set at that moment.</summary>
public readonly record struct TemperatureSample(
    DateTimeOffset Time,
    double Nozzle,
    double NozzleTarget,
    double Bed,
    double BedTarget);

/// <summary>
/// The recent temperatures of one printer, kept in memory for the chart on its page. Telemetry arrives about once a
/// second, so readings closer together than <see cref="MinInterval" /> are dropped, and only the last
/// <see cref="Capacity" /> readings are kept: the history is bounded however long the app runs. Nothing is persisted.
/// </summary>
public sealed class TemperatureHistory
{
    /// <summary>How many readings are kept.</summary>
    public const int Capacity = 600;

    /// <summary>The shortest time between two kept readings.</summary>
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1);

    private readonly Lock _gate = new();
    private readonly List<TemperatureSample> _samples = [];
    private readonly TimeProvider _timeProvider;

    public TemperatureHistory(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Raised after a reading was kept. It may be raised on any thread.</summary>
    public event EventHandler? Changed;

    /// <summary>The span of time the kept readings can cover.</summary>
    public static TimeSpan Window => MinInterval * Capacity;

    /// <summary>Adds a reading taken now, unless the previous one is too recent. Returns whether it was kept.</summary>
    public bool Add(double nozzle, double nozzleTarget, double bed, double bedTarget)
    {
        var now = _timeProvider.GetUtcNow();
        lock (_gate)
        {
            if (_samples.Count > 0 && now - _samples[^1].Time < MinInterval)
            {
                return false;
            }

            _samples.Add(new TemperatureSample(now, nozzle, nozzleTarget, bed, bedTarget));
            if (_samples.Count > Capacity)
            {
                _samples.RemoveRange(0, _samples.Count - Capacity);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Adds a reading from a telemetry snapshot, which carries only what changed: a temperature the snapshot does not
    /// mention keeps its latest known value (0 before any). A snapshot without any temperature adds nothing, so the offline
    /// message that carries none does not draw a drop to zero.
    /// </summary>
    public bool Record(double? nozzle, double? nozzleTarget, double? bed, double? bedTarget)
    {
        if (nozzle is null && nozzleTarget is null && bed is null && bedTarget is null)
        {
            return false;
        }

        TemperatureSample latest;
        lock (_gate)
        {
            latest = _samples.Count > 0 ? _samples[^1] : default;
        }

        return Add(nozzle ?? latest.Nozzle, nozzleTarget ?? latest.NozzleTarget, bed ?? latest.Bed, bedTarget ?? latest.BedTarget);
    }

    /// <summary>The current time, on the same clock as the readings.</summary>
    public DateTimeOffset Now => _timeProvider.GetUtcNow();

    /// <summary>A copy of the kept readings, oldest first.</summary>
    public IReadOnlyList<TemperatureSample> Snapshot()
    {
        lock (_gate)
        {
            return _samples.ToArray();
        }
    }
}
