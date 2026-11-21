using OctaGrid.Client.Contracts;
using OctaGrid.Client.DesignTime;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;
using OctaGrid.Client.ViewModels;
using OctaGrid.Extensions.Models;
using OctaGrid.Protocol.Messaging;
using static OctaGrid.Client.Tests.TemperatureHistoryTests;

namespace OctaGrid.Client.Tests;

public class PrinterWatcherTests
{
    private readonly FakeTelemetryClient _client = new();
    private readonly FakeMessages _messages = new();
    private readonly TemperatureHistoryStore _store = new();
    private readonly PrinterWatcher _watcher;

    public PrinterWatcherTests()
    {
        _watcher = new PrinterWatcher(_client, _messages, _store);
    }

    [Fact]
    public async Task Watching_a_printer_sends_the_set_and_fills_its_state_and_history()
    {
        var printer = Guid.NewGuid();
        _client.History = [Sample(0, 200), Sample(1, 201)];

        _watcher.Watch(printer);
        await _watcher.Synced;

        var call = Assert.Single(_client.Calls);
        Assert.Equal([printer], call.PrinterIds);
        Assert.Equal(DateTimeOffset.MinValue, call.Resume[printer]);
        Assert.Equal([200.0, 201], _store.GetOrCreate(printer).Snapshot().Select(s => Value(s)));
        Assert.Equal(UniversalPrinterState.Idle, Assert.Single(_messages.States).State);
    }

    [Fact]
    public async Task A_second_watch_asks_for_the_state_again_from_where_the_history_ends()
    {
        var printer = Guid.NewGuid();
        _client.History = [Sample(0, 200)];
        _watcher.Watch(printer);
        await _watcher.Synced;

        _watcher.Watch(printer);
        await _watcher.Synced;

        var call = _client.Calls[^1];
        Assert.Equal([printer], call.PrinterIds);
        Assert.Equal(Sample(0, 200).Time, call.Resume[printer]);
        Assert.Equal(2, _messages.States.Count);
    }

    [Fact]
    public async Task A_printer_stays_watched_until_its_last_watch_ends()
    {
        var printer = Guid.NewGuid();
        var first = _watcher.Watch(printer);
        var second = _watcher.Watch(printer);
        await _watcher.Synced;
        var calls = _client.Calls.Count;

        first.Dispose();
        first.Dispose();
        await _watcher.Synced;
        Assert.Equal(calls, _client.Calls.Count);

        second.Dispose();
        await _watcher.Synced;
        Assert.Empty(_client.Calls[^1].PrinterIds);
        Assert.Empty(_client.Calls[^1].Resume);
    }

    [Fact]
    public async Task State_that_did_not_arrive_is_asked_for_again_with_the_next_change()
    {
        var printer = Guid.NewGuid();
        _client.Fail = true;
        _watcher.Watch(printer);
        await _watcher.Synced;

        _client.Fail = false;
        _watcher.Watch(Guid.NewGuid());
        await _watcher.Synced;

        Assert.Contains(printer, _client.Calls[^1].Resume.Keys);
        Assert.Contains(_messages.States, s => s.PrinterId == printer);
    }

    [Fact]
    public async Task A_card_watches_its_printer_until_it_is_detached()
    {
        var printer = Guid.NewGuid();
        var card = new PrinterDashboardCardViewModel(
            new PrinterStatusWidget { Id = printer, Name = "A", ConnectorId = "bambu", IsConnectorAvailable = true },
            new DesignAgentCommands(),
            new ByteArrayImageCache(),
            watcher: _watcher);
        await _watcher.Synced;
        Assert.Equal([printer], _client.Calls[^1].PrinterIds);

        card.Detach();
        await _watcher.Synced;

        Assert.Empty(_client.Calls[^1].PrinterIds);
    }

    private sealed class FakeTelemetryClient : IPrinterTelemetryClient
    {
        private readonly Lock _gate = new();
        private readonly List<(IReadOnlyCollection<Guid> PrinterIds, IReadOnlyDictionary<Guid, DateTimeOffset> Resume)> _calls = [];

        public List<(IReadOnlyCollection<Guid> PrinterIds, IReadOnlyDictionary<Guid, DateTimeOffset> Resume)> Calls
        {
            get
            {
                lock (_gate)
                {
                    return _calls.ToList();
                }
            }
        }

        public bool Fail { get; set; }

        public IReadOnlyList<TelemetrySample> History { get; set; } = [];

        public Task<IReadOnlyList<PrinterBackfill>> WatchAsync(
            IReadOnlyCollection<Guid> printerIds,
            IReadOnlyDictionary<Guid, DateTimeOffset>? resume,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _calls.Add((printerIds.ToList(), resume ?? new Dictionary<Guid, DateTimeOffset>()));
            }

            if (Fail)
            {
                throw new InvalidOperationException("The backend is not reachable.");
            }

            IReadOnlyList<PrinterBackfill> backfills = (resume?.Keys ?? [])
                .Select(id => new PrinterBackfill(id, new PrinterTelemetry(id) { State = UniversalPrinterState.Idle }, History))
                .ToList();
            return Task.FromResult(backfills);
        }
    }

    private sealed class FakeMessages : IClientMessageHandler
    {
        private readonly Lock _gate = new();
        private readonly List<PrinterTelemetry> _states = [];

        public List<PrinterTelemetry> States
        {
            get
            {
                lock (_gate)
                {
                    return _states.ToList();
                }
            }
        }

        public void Handle(EntityChanged entity)
        {
        }

        public void Handle(PrinterTelemetry telemetryMessage, TelemetrySample? sample)
        {
            lock (_gate)
            {
                _states.Add(telemetryMessage);
            }
        }

        public void Handle(PrinterEventRecorded recorded)
        {
        }

        public void Handle(PrinterConsoleLineRecorded recorded)
        {
        }
    }
}
