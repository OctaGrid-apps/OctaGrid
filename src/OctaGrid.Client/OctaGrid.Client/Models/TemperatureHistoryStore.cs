using System.Collections.Concurrent;

namespace OctaGrid.Client.Models;

/// <summary>
/// The temperature history of every printer, kept for as long as the app runs. It lives apart from the dashboard cards and
/// the printer page because those are created again whenever the dashboard is shown again, and a chart that starts empty
/// each time is useless. Telemetry feeds it directly, so it also fills while some other page is open.
/// </summary>
public interface ITemperatureHistoryStore
{
    /// <summary>The history of a printer; the same object each time for the same printer.</summary>
    TemperatureHistory GetOrCreate(Guid printerId);

    /// <summary>Forgets the history of a printer that was deleted.</summary>
    void Remove(Guid printerId);
}

/// <inheritdoc />
public sealed class TemperatureHistoryStore : ITemperatureHistoryStore
{
    private readonly ConcurrentDictionary<Guid, TemperatureHistory> _histories = new();
    private readonly TimeProvider _timeProvider;

    public TemperatureHistoryStore(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public TemperatureHistory GetOrCreate(Guid printerId) =>
        _histories.GetOrAdd(printerId, _ => new TemperatureHistory(_timeProvider));

    /// <inheritdoc />
    public void Remove(Guid printerId) => _histories.TryRemove(printerId, out _);
}
