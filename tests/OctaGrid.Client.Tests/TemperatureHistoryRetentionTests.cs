using OctaGrid.Client.DesignTime;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;
using OctaGrid.Client.ViewModels;
using OctaGrid.Extensions.Models;
using OctaGrid.Protocol.Messaging;
using static OctaGrid.Client.Tests.TemperatureHistoryTests;

namespace OctaGrid.Client.Tests;

/// <summary>
/// The chart's history has to survive what the screens do: the dashboard and its cards are created again whenever the
/// dashboard is shown again, and while another page is open nothing shows the printer at all.
/// </summary>
[Collection(UiCollection.Name)]
public class TemperatureHistoryRetentionTests
{
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
        store.GetOrCreate(printer).Add(Sample(0, 200));

        store.Remove(printer);

        Assert.Empty(store.GetOrCreate(printer).Snapshot());
    }

    [Fact]
    public void The_sample_that_comes_with_telemetry_is_recorded_without_anything_showing_the_printer()
    {
        var store = new TemperatureHistoryStore();
        var handler = new MessageHandler(store);
        var printer = Guid.NewGuid();

        handler.Handle(new PrinterTelemetry(printer) { NozzleTemperature = 24 }, Sample(0, 24));

        var sample = Assert.Single(store.GetOrCreate(printer).Snapshot());
        Assert.Equal(24, Value(sample));
    }

    [Fact]
    public void Telemetry_the_agent_stored_no_sample_for_adds_nothing()
    {
        var store = new TemperatureHistoryStore();
        var handler = new MessageHandler(store);
        var printer = Guid.NewGuid();

        handler.Handle(new PrinterTelemetry(printer) { NozzleTemperature = 24 }, null);

        Assert.Empty(store.GetOrCreate(printer).Snapshot());
    }

    [Fact]
    public void A_deleted_printer_takes_its_history_with_it()
    {
        var store = new TemperatureHistoryStore();
        var handler = new MessageHandler(store);
        var printer = Guid.NewGuid();
        handler.Handle(new PrinterTelemetry(printer), Sample(0, 200));

        handler.Handle(new EntityChanged(EntityTypes.Printer, printer, 1, ChangeKind.Deleted, Guid.NewGuid()));

        Assert.Empty(store.GetOrCreate(printer).Snapshot());
    }

    [Fact]
    public void A_handler_without_a_store_still_handles_telemetry()
    {
        new MessageHandler().Handle(new PrinterTelemetry(Guid.NewGuid()), Sample(0, 200));
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
            new MessageHandler(store).Handle(new PrinterTelemetry(before[0].Item.Id), Sample(0, 200));

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
