using CommunityToolkit.Mvvm.Messaging;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Services;
using OctaGrid.Client.ViewModels;
using OctaGrid.Protocol.Agents;
using OctaGrid.Protocol.Extensions;
using OctaGrid.Protocol.Messaging;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Client.Tests;

public class PrinterConsoleViewModelTests
{
    /// <summary>Answers console requests the way the agent does and counts them; everything else is unused here.</summary>
    private sealed class FakeCommands : IAgentCommandsService
    {
        public List<ConsoleLine> Stored { get; } = [];

        public List<long?> Requests { get; } = [];

        public bool Truncated { get; set; }

        public Exception? Failure { get; set; }

        /// <summary>The agent's current log. Change it to simulate the agent starting a new one.</summary>
        public Guid StreamId { get; set; } = Guid.NewGuid();

        public Task<ConsolePage> GetPrinterConsoleAsync(Guid printerId, long? afterSequence, int limit, CancellationToken cancellationToken)
        {
            Requests.Add(afterSequence);
            if (Failure is not null)
            {
                return Task.FromException<ConsolePage>(Failure);
            }

            var latest = Stored.Count == 0 ? 0 : Stored[^1].Sequence;
            var lines = afterSequence is null
                ? Stored.TakeLast(limit).ToList()
                : Stored.Where(l => l.Sequence > afterSequence).Take(limit).ToList();
            return Task.FromResult(new ConsolePage(lines, latest, Truncated, StreamId));
        }

        public IReadOnlyList<ConnectorDescriptor> Connectors => [];

        public Task DeleteFileAsync(Guid deviceId, string fileName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PrinterEventPage> GetPrinterEventsAsync(Guid printerId, long? afterSequence, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IEnumerable<PrintMetadata>> GetPrinterFilesAsync(Guid deviceId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PrintThumbnail?> GetThumbnailAsync(Guid printerId, string fileName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task PausePrintJobAsync(Guid deviceId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ResumePrintJobAsync(Guid deviceId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task StartPrintJobAsync(Guid deviceId, string fileName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task StopPrintJobAsync(Guid deviceId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ConnectionTestResult> TestConnectionAsync(string connectorId, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task TriggerInitialStateAsync(Guid printerId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task TriggerInitialStateAsync(IEnumerable<Guid> printers, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task UploadFileAsync(Guid deviceId, Stream stream, string fileName, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static ConsoleLine Line(long sequence, string text = "line") =>
        new(sequence, Start.AddSeconds(sequence), $"{text} {sequence}");

    private static (PrinterConsoleViewModel ViewModel, FakeCommands Commands, Guid PrinterId) Create(int storedLines = 0)
    {
        var commands = new FakeCommands();
        for (var i = 1; i <= storedLines; i++)
        {
            commands.Stored.Add(Line(i));
        }

        var printerId = Guid.NewGuid();
        return (new PrinterConsoleViewModel(printerId, commands), commands, printerId);
    }

    private static void Publish(Guid printerId, Guid streamId, ConsoleLine line) =>
        WeakReferenceMessenger.Default.Send(new PrinterConsoleData(new PrinterConsoleLineRecorded(printerId, streamId, line)), printerId);

    [Fact]
    public void Nothing_is_requested_until_the_console_is_opened()
    {
        var (_, commands, _) = Create(storedLines: 3);

        Assert.Empty(commands.Requests);
    }

    [Fact]
    public async Task Loading_shows_the_oldest_line_first_and_the_newest_last()
    {
        var (viewModel, _, _) = Create(storedLines: 3);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(["line 1", "line 2", "line 3"], viewModel.Lines.Select(l => l.Text));
        Assert.False(viewModel.IsLoading);
        Assert.False(viewModel.IsEmpty);
    }

    [Fact]
    public async Task A_printer_that_wrote_nothing_says_so_instead_of_showing_a_blank_list()
    {
        var (viewModel, _, _) = Create();

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsEmpty);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task A_failed_load_shows_the_error_and_can_be_retried()
    {
        var (viewModel, commands, _) = Create(storedLines: 1);
        commands.Failure = new InvalidOperationException("boom");

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasError);
        Assert.False(viewModel.IsEmpty);
        Assert.False(viewModel.IsLoading);

        commands.Failure = null;
        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasError);
        Assert.Single(viewModel.Lines);
    }

    [Fact]
    public async Task A_console_the_agent_had_to_cut_is_marked_incomplete()
    {
        var (viewModel, commands, _) = Create(storedLines: 2);
        commands.Truncated = true;

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsIncomplete);
    }

    [Fact]
    public async Task The_next_live_line_is_added_at_the_bottom_without_asking_the_agent()
    {
        var (viewModel, commands, printerId) = Create(storedLines: 2);
        await viewModel.LoadCommand.ExecuteAsync(null);
        var requests = commands.Requests.Count;

        Publish(printerId, commands.StreamId, Line(3, "live"));

        Assert.Equal("live 3", viewModel.Lines[^1].Text);
        Assert.Equal(3, viewModel.Lines.Count);
        Assert.Equal(requests, commands.Requests.Count);
    }

    [Fact]
    public async Task A_line_that_is_already_shown_is_ignored()
    {
        var (viewModel, commands, printerId) = Create(storedLines: 2);
        await viewModel.LoadCommand.ExecuteAsync(null);

        Publish(printerId, commands.StreamId, Line(2));

        Assert.Equal(2, viewModel.Lines.Count);
    }

    [Fact]
    public async Task A_missed_line_makes_the_console_ask_the_agent_for_what_came_after_the_last_one()
    {
        var (viewModel, commands, printerId) = Create(storedLines: 2);
        await viewModel.LoadCommand.ExecuteAsync(null);
        commands.Stored.Add(Line(3));
        commands.Stored.Add(Line(4));

        // Line 3 was never delivered; line 4 arrives.
        Publish(printerId, commands.StreamId, Line(4));

        Assert.Equal(["line 1", "line 2", "line 3", "line 4"], viewModel.Lines.Select(l => l.Text));
        Assert.Equal(2, commands.Requests[^1]);
    }

    [Fact]
    public async Task A_new_log_on_the_agent_replaces_what_was_shown()
    {
        var (viewModel, commands, printerId) = Create(storedLines: 3);
        await viewModel.LoadCommand.ExecuteAsync(null);

        // The agent restarted: a new stream whose sequences already passed ours.
        commands.StreamId = Guid.NewGuid();
        commands.Stored.Clear();
        for (var i = 1; i <= 5; i++)
        {
            commands.Stored.Add(Line(i, "fresh"));
        }

        Publish(printerId, commands.StreamId, Line(5, "fresh"));

        Assert.Equal(["fresh 1", "fresh 2", "fresh 3", "fresh 4", "fresh 5"], viewModel.Lines.Select(l => l.Text));
    }

    [Fact]
    public async Task Only_the_newest_lines_are_kept_and_the_console_says_older_ones_are_gone()
    {
        var (viewModel, commands, printerId) = Create(storedLines: 1);
        await viewModel.LoadCommand.ExecuteAsync(null);

        for (var i = 2; i <= PrinterConsoleViewModel.MaxShown + 5; i++)
        {
            Publish(printerId, commands.StreamId, Line(i));
        }

        Assert.Equal(PrinterConsoleViewModel.MaxShown, viewModel.Lines.Count);
        Assert.Equal($"line {PrinterConsoleViewModel.MaxShown + 5}", viewModel.Lines[^1].Text);
        Assert.True(viewModel.IsIncomplete);
    }

    [Fact]
    public async Task Clearing_empties_the_list_and_what_was_cleared_does_not_come_back()
    {
        var (viewModel, commands, printerId) = Create(storedLines: 3);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.ClearCommand.Execute(null);
        Assert.Empty(viewModel.Lines);

        commands.Stored.Add(Line(4));
        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(["line 4"], viewModel.Lines.Select(l => l.Text));
        Assert.Equal(3, commands.Requests[^1]);
    }

    [Fact]
    public async Task Lines_that_arrive_before_the_first_load_are_ignored()
    {
        var (viewModel, commands, printerId) = Create(storedLines: 2);

        Publish(printerId, commands.StreamId, Line(1));

        Assert.Empty(viewModel.Lines);
        Assert.Empty(commands.Requests);
        await viewModel.LoadCommand.ExecuteAsync(null);
        Assert.Equal(2, viewModel.Lines.Count);
    }

    [Fact]
    public async Task A_stopped_console_ignores_new_lines()
    {
        var (viewModel, commands, printerId) = Create(storedLines: 1);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.Stop();
        Publish(printerId, commands.StreamId, Line(2));

        Assert.Single(viewModel.Lines);
        Assert.False(WeakReferenceMessenger.Default.IsRegistered<PrinterConsoleData, Guid>(viewModel, printerId));
    }

    [Fact]
    public async Task Lines_of_another_printer_are_ignored()
    {
        var (viewModel, commands, _) = Create(storedLines: 1);
        await viewModel.LoadCommand.ExecuteAsync(null);

        Publish(Guid.NewGuid(), commands.StreamId, Line(2));

        Assert.Single(viewModel.Lines);
    }
}
