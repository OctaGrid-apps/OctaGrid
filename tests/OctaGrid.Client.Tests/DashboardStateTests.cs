using CommunityToolkit.Mvvm.Messaging;
using OctaGrid.Client.DesignTime;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;
using OctaGrid.Client.ViewModels;
using OctaGrid.Extensions.Models;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Tests;

/// <summary>The dashboard keeps its cards while the user is on other pages, and reloads only what changed.</summary>
[Collection(UiCollection.Name)]
public class DashboardStateTests
{
    private static (DashboardsViewModel Page, DashboardFilterTests.FakePrintersClient Printers) Create()
    {
        var settings = new InMemorySettingsStore();
        var state = new ApplicationState(settings)
        {
            SelectedWorkspace = new Workspace { Id = DesignOctaGridClient.DefaultWorkspaceId, Name = "Default" },
        };
        var printers = new DashboardFilterTests.FakePrintersClient(("A", "bambu"), ("B", "moonraker"));
        var page = new DashboardsViewModel(
            new DesignOctaGridClient(), printers, state, new DesignAgentCommands(), new ByteArrayImageCache(), settings);
        return (page, printers);
    }

    private static void PrinterChanged(Guid printerId) =>
        WeakReferenceMessenger.Default.Send(new PrinterEntityUpsert(
            new EntityChanged(EntityTypes.Printer, printerId, 2, ChangeKind.Upserted, DesignOctaGridClient.DefaultWorkspaceId)));

    [Fact]
    public async Task Showing_the_page_again_keeps_the_cards_and_what_they_know()
    {
        var (page, _) = Create();
        await page.ShowCommand.ExecuteAsync(null);
        var cards = page.Printers.ToList();
        cards[0].Item.State = UniversalPrinterState.Printing;

        await page.ShowCommand.ExecuteAsync(null);

        Assert.Equal(cards, page.Printers);
        Assert.Equal(UniversalPrinterState.Printing, page.Printers[0].Item.State);
    }

    [Fact]
    public async Task A_printer_changed_elsewhere_is_picked_up_and_the_other_cards_are_kept()
    {
        var (page, printers) = Create();
        await page.ShowCommand.ExecuteAsync(null);
        var kept = page.Printers[0];
        kept.Item.State = UniversalPrinterState.Idle;

        var b = printers.Printers[1];
        printers.Printers[1] = b with { Name = "B renamed" };
        printers.Printers.Add(printers.Printers[0] with { Id = Guid.NewGuid(), Name = "C" });
        PrinterChanged(b.Id);
        await page.ShowCommand.ExecuteAsync(null);

        Assert.Equal(["A", "B renamed", "C"], page.Printers.Select(p => p.Item.Name));
        Assert.Same(kept, page.Printers[0]);
        Assert.Equal(UniversalPrinterState.Idle, kept.Item.State);
    }

    [Fact]
    public async Task A_printer_moved_to_another_connector_gets_a_new_card()
    {
        var (page, printers) = Create();
        await page.ShowCommand.ExecuteAsync(null);
        var before = page.Printers[1];

        printers.Printers[1] = printers.Printers[1] with { ConnectorId = "bambu" };
        PrinterChanged(printers.Printers[1].Id);
        await page.ShowCommand.ExecuteAsync(null);

        Assert.NotSame(before, page.Printers[1]);
    }

    [Fact]
    public async Task A_removed_printer_card_stops_receiving_telemetry()
    {
        var (page, printers) = Create();
        await page.ShowCommand.ExecuteAsync(null);
        var removed = page.Printers[1];

        printers.Printers.RemoveAt(1);
        PrinterChanged(removed.Item.Id);
        await page.ShowCommand.ExecuteAsync(null);
        WeakReferenceMessenger.Default.Send(
            new PrinterTelemetryData(new PrinterTelemetry(removed.Item.Id) { State = UniversalPrinterState.Printing }),
            removed.Item.Id);

        Assert.DoesNotContain(removed, page.Printers);
        Assert.False(removed.Item.IsStateKnown);
    }

    [Fact]
    public async Task Refresh_starts_every_card_over()
    {
        var (page, _) = Create();
        await page.ShowCommand.ExecuteAsync(null);
        var before = page.Printers.ToList();

        await page.RefreshPrintersCommand.ExecuteAsync(null);

        Assert.All(page.Printers, card => Assert.DoesNotContain(card, before));
    }
}
