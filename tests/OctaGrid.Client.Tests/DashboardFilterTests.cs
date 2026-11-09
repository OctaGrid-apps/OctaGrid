using OctaGrid.Client.Contracts;
using OctaGrid.Client.DesignTime;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;
using OctaGrid.Client.ViewModels;
using OctaGrid.Protocol.Messaging;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Client.Tests;

[Collection(UiCollection.Name)]
public class DashboardFilterTests
{
    private sealed class FakePrintersClient : IPrintersClient
    {
        private readonly List<PrinterResponse> _printers;

        public FakePrintersClient(params (string Name, string ConnectorId)[] printers) =>
            _printers = printers
                .Select(p => new PrinterResponse(Guid.NewGuid(), p.Name, p.ConnectorId, new Dictionary<string, string>(), new Dictionary<string, string>(), "", true, 1))
                .ToList();

        public Task<List<PrinterResponse>> GetPrintersList(Guid workspaceId, GetPrintersQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(_printers.ToList());

        public Task<PrinterResponse> CreatePrinter(Guid workspaceId, CreatePrinterRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeletePrinter(Guid printerId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PrinterResponse> GetPrinter(Guid workspaceId, Guid printerId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SetPrinterEnabled(Guid printerId, bool state, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PrinterResponse> UpdateIdentity(Guid printerId, UpdatePrinterIdentityRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PrinterResponse> UpdatePrinter(Guid printerId, UpdatePrinterRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static async Task<DashboardsViewModel> Page(InMemorySettingsStore settings, bool reportOffline = true)
    {
        var state = new ApplicationState(settings)
        {
            SelectedWorkspace = new Workspace { Id = DesignOctaGridClient.DefaultWorkspaceId, Name = "Default" },
        };
        var viewModel = new DashboardsViewModel(
            new DesignOctaGridClient(),
            new FakePrintersClient(("A", "bambu"), ("B", "bambu"), ("C", "moonraker")),
            state,
            new DesignAgentCommands(),
            new ByteArrayImageCache(),
            settings);
        await viewModel.LoadDataCommand.ExecuteAsync(null);
        if (reportOffline)
        {
            foreach (var card in viewModel.Printers.ToList())
            {
                card.Item.State = UniversalPrinterState.Offline;
            }
        }

        return viewModel;
    }

    private static int Count(DashboardsViewModel vm, UniversalPrinterState state) =>
        vm.StatusFilters.Single(f => f.State == state).Count;

    [Fact]
    public async Task Badges_count_printers_per_status_and_clicking_one_filters_the_list()
    {
        var vm = await Page(new InMemorySettingsStore());
        vm.Printers[0].Item.State = UniversalPrinterState.Idle;
        vm.Printers[1].Item.State = UniversalPrinterState.Printing;

        Assert.Equal(1, Count(vm, UniversalPrinterState.Idle));
        Assert.Equal(1, Count(vm, UniversalPrinterState.Printing));
        Assert.Equal(1, Count(vm, UniversalPrinterState.Offline));
        Assert.Equal(0, Count(vm, UniversalPrinterState.Paused));

        vm.StatusFilters.Single(f => f.State == UniversalPrinterState.Printing).IsSelected = true;

        Assert.Equal(["B"], vm.Printers.Select(p => p.Item.Name));
        Assert.False(vm.IsFilterHidingAll);
    }

    [Fact]
    public async Task Badges_for_states_without_printers_are_hidden_unless_selected()
    {
        var vm = await Page(new InMemorySettingsStore());
        var offline = vm.StatusFilters.Single(f => f.State == UniversalPrinterState.Offline);
        var paused = vm.StatusFilters.Single(f => f.State == UniversalPrinterState.Paused);

        Assert.True(offline.IsVisible);
        Assert.False(paused.IsVisible);

        paused.IsSelected = true;
        Assert.True(paused.IsVisible);

        paused.IsSelected = false;
        Assert.False(paused.IsVisible);
    }

    [Fact]
    public async Task All_badges_stay_visible_until_every_printer_has_reported_a_state()
    {
        var vm = await Page(new InMemorySettingsStore(), reportOffline: false);
        // The design-time agent has no connectors, so mark them available: only those printers are expected to report.
        foreach (var card in vm.Printers)
        {
            card.Item.IsConnectorAvailable = true;
        }

        Assert.All(vm.StatusFilters, f => Assert.True(f.IsVisible));

        vm.Printers[0].Item.State = UniversalPrinterState.Idle;
        vm.Printers[1].Item.State = UniversalPrinterState.Idle;
        Assert.All(vm.StatusFilters, f => Assert.True(f.IsVisible));

        vm.Printers[2].Item.State = UniversalPrinterState.Idle;
        Assert.Equal(
            [UniversalPrinterState.Idle],
            vm.StatusFilters.Where(f => f.IsVisible).Select(f => f.State));
    }

    [Fact]
    public void The_start_pause_label_follows_the_printer_state()
    {
        var widget = new PrinterStatusWidget { State = UniversalPrinterState.Idle };
        var changed = new List<string?>();
        widget.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        widget.State = UniversalPrinterState.Printing;

        Assert.Contains(nameof(PrinterStatusWidget.StartPauseLabel), changed);
        Assert.Equal("Pause", widget.StartPauseLabel);
    }

    [Fact]
    public async Task Selecting_another_badge_replaces_the_filter_and_selecting_it_again_clears_it()
    {
        var vm = await Page(new InMemorySettingsStore());
        var idle = vm.StatusFilters.Single(f => f.State == UniversalPrinterState.Idle);
        var offline = vm.StatusFilters.Single(f => f.State == UniversalPrinterState.Offline);

        idle.IsSelected = true;
        Assert.True(vm.IsFilterHidingAll);
        offline.IsSelected = true;

        Assert.False(idle.IsSelected);
        Assert.Equal(3, vm.Printers.Count);

        offline.IsSelected = false;
        Assert.Equal(3, vm.Printers.Count);
    }

    [Fact]
    public async Task A_printer_changing_state_moves_between_badges_and_leaves_the_filtered_list()
    {
        var vm = await Page(new InMemorySettingsStore());
        vm.StatusFilters.Single(f => f.State == UniversalPrinterState.Offline).IsSelected = true;
        var first = vm.Printers[0];

        first.Item.State = UniversalPrinterState.Idle;

        Assert.DoesNotContain(first, vm.Printers);
        Assert.Equal(2, Count(vm, UniversalPrinterState.Offline));
        Assert.Equal(1, Count(vm, UniversalPrinterState.Idle));
    }

    [Fact]
    public async Task The_connector_dropdown_filters_printers_and_narrows_the_badge_counts()
    {
        var vm = await Page(new InMemorySettingsStore());

        Assert.Equal(["All printers", "bambu", "moonraker"], vm.ConnectorOptions.Select(o => o.DisplayName));

        vm.SelectedConnector = vm.ConnectorOptions.Single(o => o.ConnectorId == "moonraker");

        Assert.Equal(["C"], vm.Printers.Select(p => p.Item.Name));
        Assert.Equal(1, Count(vm, UniversalPrinterState.Offline));
    }

    [Fact]
    public async Task The_selected_filters_are_saved_and_applied_after_a_restart()
    {
        var settings = new InMemorySettingsStore();
        var vm = await Page(settings);
        vm.StatusFilters.Single(f => f.State == UniversalPrinterState.Offline).IsSelected = true;
        vm.SelectedConnector = vm.ConnectorOptions.Single(o => o.ConnectorId == "bambu");

        Assert.Equal(UniversalPrinterState.Offline, settings.ClientSettings.DashboardStatusFilter);
        Assert.Equal("bambu", settings.ClientSettings.DashboardConnectorFilter);

        var restarted = await Page(new InMemorySettingsStore(settings.ClientSettings));

        Assert.Equal("bambu", restarted.SelectedConnector.ConnectorId);
        Assert.True(restarted.StatusFilters.Single(f => f.State == UniversalPrinterState.Offline).IsSelected);
        Assert.Equal(["A", "B"], restarted.Printers.Select(p => p.Item.Name));
    }

    [Fact]
    public async Task A_saved_status_filter_does_not_hide_printers_that_have_not_reported_yet()
    {
        var settings = new InMemorySettingsStore(new ClientSettings { DashboardStatusFilter = UniversalPrinterState.Printing });

        var vm = await Page(settings, reportOffline: false);

        Assert.Equal(3, vm.Printers.Count);
        Assert.False(vm.IsFilterHidingAll);
        Assert.Equal(0, Count(vm, UniversalPrinterState.Offline));

        vm.Printers[0].Item.State = UniversalPrinterState.Idle;

        Assert.Equal(2, vm.Printers.Count);
    }

    [Fact]
    public async Task A_saved_connector_without_printers_falls_back_to_all_printers()
    {
        var settings = new InMemorySettingsStore(new ClientSettings { DashboardConnectorFilter = "gone" });

        var vm = await Page(settings);

        Assert.Null(vm.SelectedConnector.ConnectorId);
        Assert.Equal(3, vm.Printers.Count);
        Assert.Equal("gone", settings.ClientSettings.DashboardConnectorFilter);
    }
}
