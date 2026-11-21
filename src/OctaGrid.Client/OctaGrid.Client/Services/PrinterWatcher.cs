using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Models;

namespace OctaGrid.Client.Services;

/// <summary>
/// Keeps the backend told which printers this client shows. Whatever shows a printer holds a watch on it for as long as
/// it does. Each new watch gets the printer's current state, even when something else already watched it, because a
/// card that starts out empty would otherwise wait for the printer's next report. The state and history the backend
/// returns go where live telemetry goes: the state through <see cref="IClientMessageHandler"/>, the history into the
/// <see cref="ITemperatureHistoryStore"/>.
/// </summary>
public sealed class PrinterWatcher
{
    private readonly PrinterTelemetryArrivals? _arrivals;
    private readonly IPrinterTelemetryClient _client;
    private readonly Dictionary<Guid, int> _counts = [];
    private readonly Lock _gate = new();
    private readonly ITemperatureHistoryStore? _histories;
    private readonly ILogger<PrinterWatcher> _logger;
    private readonly IClientMessageHandler _messages;
    private readonly HashSet<Guid> _needState = [];
    private bool _changed;
    private Task _sync = Task.CompletedTask;
    private bool _syncing;

    public PrinterWatcher(
        IPrinterTelemetryClient client,
        IClientMessageHandler messages,
        ITemperatureHistoryStore? histories = null,
        ILogger<PrinterWatcher>? logger = null,
        PrinterTelemetryArrivals? arrivals = null)
    {
        _client = client;
        _messages = messages;
        _histories = histories;
        _arrivals = arrivals;
        _logger = logger ?? NullLogger<PrinterWatcher>.Instance;
    }

    /// <summary>Completes when the backend has the current set. For tests.</summary>
    internal Task Synced
    {
        get
        {
            lock (_gate)
            {
                return _sync;
            }
        }
    }

    /// <summary>
    /// Starts watching a printer and asks for its current state. Dispose the result to stop; the printer stays watched
    /// while anything else watches it.
    /// </summary>
    public IDisposable Watch(Guid printerId)
    {
        lock (_gate)
        {
            _counts[printerId] = _counts.GetValueOrDefault(printerId) + 1;
            _needState.Add(printerId);
        }

        RequestSync();
        return new Lease(this, printerId);
    }

    private void Release(Guid printerId)
    {
        lock (_gate)
        {
            if (--_counts[printerId] > 0)
            {
                return;
            }

            _counts.Remove(printerId);
            _needState.Remove(printerId);
        }

        RequestSync();
    }

    // One call at a time, always with the latest set: changes made while a call runs are sent by the next one, so a
    // dashboard that adds its cards one by one sends a couple of calls, not one per card.
    private void RequestSync()
    {
        lock (_gate)
        {
            _changed = true;
            if (!_syncing)
            {
                _syncing = true;
                _sync = Task.Run(SyncAsync);
            }
        }
    }

    private async Task SyncAsync()
    {
        while (true)
        {
            Guid[] printerIds;
            Guid[] needState;
            lock (_gate)
            {
                if (!_changed)
                {
                    _syncing = false;
                    return;
                }

                _changed = false;
                printerIds = _counts.Keys.ToArray();
                needState = _needState.ToArray();
                _needState.Clear();
            }

            // Every printer that needs its state is listed with where the history this client has ends, so the backend
            // returns it even when it already streams that printer, with only the history that is missing.
            var resume = needState.ToDictionary(
                id => id,
                id => _histories?.GetOrCreate(id).LastTime ?? DateTimeOffset.MinValue);

            // Live telemetry that arrives while the call runs is newer than the state the answer carries.
            var arrivedBefore = needState.ToDictionary(id => id, id => _arrivals?.Count(id) ?? 0);

            try
            {
                var backfills = await _client.WatchAsync(printerIds, resume, CancellationToken.None);
                foreach (var backfill in backfills)
                {
                    _histories?.GetOrCreate(backfill.PrinterId).Merge(backfill.History);
                    if ((_arrivals?.Count(backfill.PrinterId) ?? 0) == arrivedBefore.GetValueOrDefault(backfill.PrinterId))
                    {
                        _messages.Handle(backfill.Current, sample: null);
                    }
                }
            }
            catch (Exception ex)
            {
                // Nothing shows this to the user: the printers stay "Connecting". The next change sends the set again and
                // asks again for the state that did not arrive.
                _logger.LogError(ex, "Could not tell the backend which {PrinterCount} printer(s) are shown", printerIds.Length);
                lock (_gate)
                {
                    _needState.UnionWith(needState.Where(_counts.ContainsKey));
                }
            }
        }
    }

    private sealed class Lease : IDisposable
    {
        private readonly Guid _printerId;
        private PrinterWatcher? _watcher;

        public Lease(PrinterWatcher watcher, Guid printerId)
        {
            _watcher = watcher;
            _printerId = printerId;
        }

        public void Dispose() => Interlocked.Exchange(ref _watcher, null)?.Release(_printerId);
    }
}
