using OctaGrid.Client.DesignTime;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;
using OctaGrid.Client.ViewModels;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Tests;

/// <summary>
/// The chart's history has to survive what the screens do: the dashboard and its cards are created again whenever the
/// dashboard is shown again, and while another page is open nothing shows the printer at all.
/// </summary>
[Collection(UiCollection.Name)]
public class TemperatureHistoryRetentionTests
{
    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private static PrinterTelemetry Telemetry(Guid printerId, double? nozzle = null, double? nozzleTarget = null, double? bed = null, double? bedTarget = null) =>
        new(printerId)
        {
            NozzleTemperature = nozzle,
            NozzleTargetTemperature = nozzleTarget,
            BedTemperature = bed,
            BedTargetTemperature = bedTarget,
        };

    [Fact]
    public void The_store_gives_the_same_history_for_the_same_printer_and_another_one_for_another_printer()
    {
        var store = new TemperatureHistoryStore();
        var first = Guid.NewGuid();

        Assert.Same(store.GetOrCreate(first), store.GetOrCreate(first));
        Assert.NotSame(store.GetOrCreate(first), store.GetOrCreate(Guid.NewGuid()));
    }

    [Fact]
    public void A_removed_printer_starts_over_with_an_empty_history()
    {
        var store = new TemperatureHistoryStore();
        var printer = Guid.NewGuid();
        store.GetOrCreate(printer).Add(200, 210, 60, 60);

        store.Remove(printer);

        Assert.Empty(store.GetOrCreate(printer).Snapshot());
    }

    [Fact]
    public void A_snapshot_that_carries_only_some_temperatures_keeps_the_others_as_they_were()
    {
        var time = new ManualTime();
        var history = new TemperatureHistory(time);
        history.Record(200, 210, 60, 60);
        time.Advance(TimeSpan.FromSeconds(1));

        history.Record(205, null, null, null);

        var latest = history.Snapshot()[^1];
        Assert.Equal((205, 210, 60, 60), (latest.Nozzle, latest.NozzleTarget, latest.Bed, latest.BedTarget));
    }

    [Fact]
    public void A_snapshot_without_any_temperature_adds_nothing_so_going_offline_draws_no_drop_to_zero()
    {
        var history = new TemperatureHistory(new ManualTime());
        history.Record(200, 210, 60, 60);

        Assert.False(history.Record(null, null, null, null));

        Assert.Single(history.Snapshot());
    }

    [Fact]
    public void Telemetry_is_recorded_without_anything_showing_the_printer()
    {
        var store = new TemperatureHistoryStore();
        var handler = new MessageHandler(store);
        var printer = Guid.NewGuid();

        handler.Handle(Telemetry(printer, nozzle: 24, nozzleTarget: 0, bed: 24, bedTarget: 0));

        var sample = Assert.Single(store.GetOrCreate(printer).Snapshot());
        Assert.Equal(24, sample.Nozzle);
    }

    [Fact]
    public void The_offline_message_that_carries_no_temperatures_is_not_recorded()
    {
        var store = new TemperatureHistoryStore();
        var handler = new MessageHandler(store);
        var printer = Guid.NewGuid();

        handler.Handle(new PrinterTelemetry(printer));

        Assert.Empty(store.GetOrCreate(printer).Snapshot());
    }

    [Fact]
    public void A_deleted_printer_takes_its_history_with_it()
    {
        var store = new TemperatureHistoryStore();
        var handler = new MessageHandler(store);
        var printer = Guid.NewGuid();
        handler.Handle(Telemetry(printer, 200, 210, 60, 60));

        handler.Handle(new EntityChanged(EntityTypes.Printer, printer, 1, ChangeKind.Deleted, Guid.NewGuid()));

        Assert.Empty(store.GetOrCreate(printer).Snapshot());
    }

    [Fact]
    public void A_handler_without_a_store_still_handles_telemetry()
    {
        new MessageHandler().Handle(Telemetry(Guid.NewGuid(), 200, 210, 60, 60));
    }

    [Fact]
    public async Task Showing_the_dashboard_again_keeps_the_history_of_every_printer()
    {
        await HeadlessUi.Run(async () =>
        {
            var store = new TemperatureHistoryStore();
            var settings = new InMemorySettingsStore();
            var state = new ApplicationState(settings)
            {
                SelectedWorkspace = new Workspace { Id = DesignOctaGridClient.DefaultWorkspaceId, Name = "Default" },
            };
            var viewModel = new DashboardsViewModel(
                new DesignOctaGridClient(),
                new DashboardFilterTests.FakePrintersClient(("A", "bambu"), ("B", "moonraker")),
                state,
                new DesignAgentCommands(),
                new ByteArrayImageCache(),
                settings,
                temperatureHistories: store);
            await viewModel.LoadDataCommand.ExecuteAsync(null);
            var before = viewModel.Printers.ToList();
            new MessageHandler(store).Handle(Telemetry(before[0].Item.Id, 200, 210, 60, 60));

            // The user went to another page and came back: the dashboard loads again and makes new cards.
            await viewModel.LoadDataCommand.ExecuteAsync(null);
            var after = viewModel.Printers.ToList();

            Assert.NotSame(before[0], after[0]);
            Assert.Same(before[0].TemperatureHistory, after[0].TemperatureHistory);
            Assert.Single(after[0].TemperatureHistory.Snapshot());
            Assert.Empty(after[1].TemperatureHistory.Snapshot());
            Assert.NotSame(after[0].TemperatureHistory, after[1].TemperatureHistory);
        });
    }
}
