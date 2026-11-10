using CommunityToolkit.Mvvm.Messaging;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;
using OctaGrid.Client.ViewModels;
using OctaGrid.Protocol.Agents;
using OctaGrid.Protocol.Exceptions;
using OctaGrid.Protocol.Extensions;
using OctaGrid.Protocol.Messaging;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Client.Tests;

/// <summary>The Files and Events tabs of the printer page: what they ask the printer for, and when.</summary>
public class PrinterPageTabsTests
{
    /// <summary>Answers instantly and records what was asked, so a test can act without waiting.</summary>
    private sealed class FakeCommands : IAgentCommandsService
    {
        public List<PrintMetadata> Files { get; } = [];

        public int FileRequests { get; private set; }

        public int EventRequests { get; private set; }

        public List<string> Started { get; } = [];

        public Exception? FilesFailure { get; set; }

        public List<ConnectorDescriptor> ConnectorList { get; } = [];

        public IReadOnlyList<ConnectorDescriptor> Connectors => ConnectorList;

        public Task<IEnumerable<PrintMetadata>> GetPrinterFilesAsync(Guid deviceId, CancellationToken cancellationToken)
        {
            FileRequests++;
            return FilesFailure is { } failure
                ? Task.FromException<IEnumerable<PrintMetadata>>(failure)
                : Task.FromResult<IEnumerable<PrintMetadata>>(Files.ToList());
        }

        public int ConsoleRequests { get; private set; }

        public Task SendConsoleCommandAsync(Guid printerId, string command, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ConsolePage> GetPrinterConsoleAsync(Guid printerId, long? afterSequence, int limit, CancellationToken cancellationToken)
        {
            ConsoleRequests++;
            return Task.FromResult(new ConsolePage([], 0, false, Guid.NewGuid()));
        }

        public Task<PrinterEventPage> GetPrinterEventsAsync(Guid printerId, long? afterSequence, int limit, CancellationToken cancellationToken)
        {
            EventRequests++;
            return Task.FromResult(new PrinterEventPage([], 0, false, Guid.NewGuid()));
        }

        public Task StartPrintJobAsync(Guid deviceId, string fileName, CancellationToken cancellationToken)
        {
            Started.Add(fileName);
            return Task.CompletedTask;
        }

        public Task<PrintThumbnail?> GetThumbnailAsync(Guid printerId, string fileName, CancellationToken cancellationToken) =>
            Task.FromResult<PrintThumbnail?>(null);

        public Task TriggerInitialStateAsync(Guid printerId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task TriggerInitialStateAsync(IEnumerable<Guid> printers, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteFileAsync(Guid deviceId, string fileName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task PausePrintJobAsync(Guid deviceId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ResumePrintJobAsync(Guid deviceId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task StopPrintJobAsync(Guid deviceId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ConnectionTestResult> TestConnectionAsync(string connectorId, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UploadFileAsync(Guid deviceId, Stream stream, string fileName, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private const string ConnectorId = "acme.printer";

    private static (PrinterPageViewModel Page, FakeCommands Commands, PrinterStatusWidget Item) Create(
        ConnectorCapabilities capabilities = ConnectorCapabilities.None)
    {
        var commands = new FakeCommands();
        commands.ConnectorList.Add(new ConnectorDescriptor(ConnectorId, "Acme printer", "1.0.0", "Acme", capabilities, [], []));
        var item = new PrinterStatusWidget { Id = Guid.NewGuid(), Name = "Voron", State = UniversalPrinterState.Idle, ConnectorId = ConnectorId };
        var card = new PrinterDashboardCardViewModel(item, commands, new ByteArrayImageCache());
        return (new PrinterPageViewModel(card, () => { }, new InMemorySettingsStore()), commands, item);
    }

    private static PrintMetadata File(string name) => new(name, 1024, "PLA", 0.4, 600);

    [Fact]
    public void Nothing_is_requested_from_the_printer_until_a_tab_that_needs_it_is_shown()
    {
        var (_, commands, _) = Create();

        Assert.Equal(0, commands.FileRequests);
        Assert.Equal(0, commands.EventRequests);
    }

    [Fact]
    public void Showing_the_Files_tab_loads_the_files_each_time()
    {
        var (page, commands, _) = Create();
        commands.Files.Add(File("benchy.gcode"));

        page.SelectedTabIndex = (int)PrinterPageTab.Files;
        page.SelectedTabIndex = (int)PrinterPageTab.Overview;
        page.SelectedTabIndex = (int)PrinterPageTab.Files;

        Assert.Equal(2, commands.FileRequests);
        Assert.Equal(["benchy.gcode"], page.Files.Files.Select(f => f.Metadata.FileName));
    }

    [Fact]
    public void Showing_the_Events_tab_loads_the_events_only_once()
    {
        var (page, commands, _) = Create();

        page.SelectedTabIndex = (int)PrinterPageTab.Events;
        page.SelectedTabIndex = (int)PrinterPageTab.Overview;
        page.SelectedTabIndex = (int)PrinterPageTab.Events;

        Assert.Equal(1, commands.EventRequests);
        Assert.True(page.PrinterEvents.IsLoaded);
    }

    [Fact]
    public void Showing_the_Overview_tab_asks_the_printer_for_nothing()
    {
        var (page, commands, _) = Create();

        page.SelectedTabIndex = (int)PrinterPageTab.Files;
        page.SelectedTabIndex = (int)PrinterPageTab.Overview;

        Assert.Equal(1, commands.FileRequests);
        Assert.Equal(0, commands.EventRequests);
    }

    [Fact]
    public void The_connector_name_falls_back_to_its_id_when_no_connector_with_that_id_is_loaded()
    {
        var (page, _, item) = Create();
        item.ConnectorId = "some.connector";

        Assert.Equal("some.connector", page.ConnectorName);
    }

    [Fact]
    public void The_console_tab_is_offered_only_for_a_connector_that_reports_a_console()
    {
        Assert.True(Create(ConnectorCapabilities.PrintControl | ConnectorCapabilities.Console).Page.HasConsole);
        Assert.False(Create(ConnectorCapabilities.PrintControl).Page.HasConsole);
    }

    [Fact]
    public void Showing_the_Console_tab_loads_the_console_only_once()
    {
        var (page, commands, _) = Create(ConnectorCapabilities.Console);

        page.SelectedTabIndex = (int)PrinterPageTab.Console;
        page.SelectedTabIndex = (int)PrinterPageTab.Overview;
        page.SelectedTabIndex = (int)PrinterPageTab.Console;

        Assert.Equal(1, commands.ConsoleRequests);
        Assert.True(page.PrinterConsole.IsLoaded);
    }

    [Fact]
    public void A_printer_without_a_console_is_never_asked_for_one()
    {
        var (page, commands, _) = Create(ConnectorCapabilities.PrintControl);

        page.SelectedTabIndex = (int)PrinterPageTab.Console;

        Assert.Equal(0, commands.ConsoleRequests);
    }

    [Fact]
    public void The_console_follows_Expert_mode_while_the_page_is_open_and_stops_following_when_it_closes()
    {
        var store = new InMemorySettingsStore(new ClientSettings { IsExpertMode = true });
        var commands = new FakeCommands();
        commands.ConnectorList.Add(new ConnectorDescriptor(
            ConnectorId, "Acme printer", "1.0.0", "Acme", ConnectorCapabilities.Console | ConnectorCapabilities.ConsoleCommands, [], []));
        var item = new PrinterStatusWidget { Id = Guid.NewGuid(), ConnectorId = ConnectorId };
        var page = new PrinterPageViewModel(new PrinterDashboardCardViewModel(item, commands, new ByteArrayImageCache()), () => { }, store);

        Assert.True(page.PrinterConsole.IsExpertMode);
        Assert.True(page.PrinterConsole.CanSend);

        store.Update(s => s with { IsExpertMode = false });
        Assert.False(page.PrinterConsole.CanSend);
        Assert.True(page.PrinterConsole.ShowExpertModeHint);

        page.Stop();
        store.Update(s => s with { IsExpertMode = true });
        Assert.False(page.PrinterConsole.IsExpertMode);
    }

    [Fact]
    public void A_connector_that_only_reports_a_console_offers_no_command_box()
    {
        var store = new InMemorySettingsStore(new ClientSettings { IsExpertMode = true });
        var commands = new FakeCommands();
        commands.ConnectorList.Add(new ConnectorDescriptor(ConnectorId, "Acme printer", "1.0.0", "Acme", ConnectorCapabilities.Console, [], []));
        var item = new PrinterStatusWidget { Id = Guid.NewGuid(), ConnectorId = ConnectorId };
        var page = new PrinterPageViewModel(new PrinterDashboardCardViewModel(item, commands, new ByteArrayImageCache()), () => { }, store);

        Assert.True(page.HasConsole);
        Assert.False(page.PrinterConsole.SupportsCommands);
        Assert.False(page.PrinterConsole.CanSend);
        Assert.False(page.PrinterConsole.ShowExpertModeHint);
    }

    [Fact]
    public void Stopping_the_page_stops_the_console_too()
    {
        var (page, _, item) = Create(ConnectorCapabilities.Console);
        Assert.True(WeakReferenceMessenger.Default.IsRegistered<PrinterConsoleData, Guid>(page.PrinterConsole, item.Id));

        page.Stop();

        Assert.False(WeakReferenceMessenger.Default.IsRegistered<PrinterConsoleData, Guid>(page.PrinterConsole, item.Id));
    }

    [Fact]
    public void A_printer_without_files_is_shown_as_empty_not_as_a_failure()
    {
        var (page, _, _) = Create();

        page.SelectedTabIndex = (int)PrinterPageTab.Files;

        Assert.True(page.Files.IsEmpty);
        Assert.False(page.Files.HasError);
    }

    [Fact]
    public async Task A_failed_file_load_is_reported_by_the_tab_and_not_as_an_empty_printer()
    {
        var (page, commands, _) = Create();
        commands.FilesFailure = new InvalidOperationException("boom");

        await Assert.ThrowsAsync<InvalidOperationException>(() => page.Files.LoadDataCommand.ExecuteAsync(null));

        Assert.True(page.Files.HasError);
        Assert.Equal("The files could not be loaded.", page.Files.ErrorMessage);
        Assert.False(page.Files.IsEmpty);
    }

    [Fact]
    public void Every_file_is_a_row_and_the_rows_follow_the_file_list()
    {
        var (page, commands, _) = Create();
        commands.Files.Add(File("benchy.gcode"));
        commands.Files.Add(File("cube.gcode"));

        page.SelectedTabIndex = (int)PrinterPageTab.Files;
        Assert.Equal(["benchy.gcode", "cube.gcode"], page.FileRows.Select(r => r.File.Metadata.FileName));

        commands.Files.RemoveAt(0);
        page.SelectedTabIndex = (int)PrinterPageTab.Overview;
        page.SelectedTabIndex = (int)PrinterPageTab.Files;

        Assert.Equal(["cube.gcode"], page.FileRows.Select(r => r.File.Metadata.FileName));
    }

    [Fact]
    public void A_row_can_print_only_while_the_printer_is_ready()
    {
        var (page, commands, item) = Create();
        commands.Files.Add(File("benchy.gcode"));
        page.SelectedTabIndex = (int)PrinterPageTab.Files;
        var row = Assert.Single(page.FileRows);

        Assert.True(row.PrintCommand.CanExecute(null));

        item.State = UniversalPrinterState.Printing;
        Assert.False(row.PrintCommand.CanExecute(null));

        item.State = UniversalPrinterState.Completed;
        Assert.True(row.PrintCommand.CanExecute(null));
        Assert.Empty(commands.Started);
    }

    [Fact]
    public async Task Printing_from_a_row_starts_that_file_and_returns_to_the_overview()
    {
        var (page, commands, item) = Create();
        commands.Files.Add(File("benchy.gcode"));
        commands.Files.Add(File("cube.gcode"));
        page.SelectedTabIndex = (int)PrinterPageTab.Files;

        await page.FileRows[1].PrintCommand.ExecuteAsync(null);

        Assert.Equal(["cube.gcode"], commands.Started);
        Assert.Equal("cube.gcode", item.PrintFileName);
        Assert.Equal((int)PrinterPageTab.Overview, page.SelectedTabIndex);
    }

    [Theory]
    [InlineData("PLA", 0.4, "PLA • {n} mm")]
    [InlineData("PETG", null, "PETG")]
    [InlineData(null, 0.6, "{n} mm")]
    [InlineData("", null, "")]
    public void A_row_shows_material_and_nozzle_under_the_name(string? material, double? nozzle, string expected)
    {
        var file = new PrintMetadataListItem(new PrintMetadata("a.gcode", 1, material, nozzle, 60), []);
        var row = new PrinterFileRowViewModel(file, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask, () => true);

        // The nozzle follows the user's number format (a comma in many languages), so the expectation does too.
        var nozzleText = nozzle?.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;
        Assert.Equal(expected.Replace("{n}", nozzleText, StringComparison.Ordinal), row.DetailsText);
    }

    [Fact]
    public void Stopping_the_page_stops_listening_for_the_printers_events()
    {
        var (page, _, item) = Create();
        Assert.True(WeakReferenceMessenger.Default.IsRegistered<PrinterEventData, Guid>(page.PrinterEvents, item.Id));

        page.Stop();
        page.Stop();

        Assert.False(WeakReferenceMessenger.Default.IsRegistered<PrinterEventData, Guid>(page.PrinterEvents, item.Id));
    }

    [Fact]
    public void A_stopped_page_does_not_load_the_events_when_its_tab_is_shown()
    {
        var (page, commands, _) = Create();
        page.Stop();

        page.SelectedTabIndex = (int)PrinterPageTab.Events;

        Assert.Equal(0, commands.EventRequests);
    }

    [Fact]
    public void An_AppException_from_the_printer_is_shown_with_its_own_message()
    {
        var (page, commands, _) = Create();
        commands.FilesFailure = new NotFoundException("Printer not found");

        page.SelectedTabIndex = (int)PrinterPageTab.Files;

        Assert.Equal("Printer not found", page.Files.ErrorMessage);
    }
}
