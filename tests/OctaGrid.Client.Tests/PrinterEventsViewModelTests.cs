using CommunityToolkit.Mvvm.Messaging;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Services;
using OctaGrid.Client.ViewModels;
using OctaGrid.Protocol.Agents;
using OctaGrid.Protocol.Extensions;
using OctaGrid.Protocol.Messaging;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Client.Tests;

public class PrinterEventsViewModelTests
{
    /// <summary>Answers event requests the way the agent does and counts them; everything else is unused here.</summary>
    private sealed class FakeCommands : IAgentCommandsService
    {
        public List<PrinterEvent> Stored { get; } = [];

        public List<long?> Requests { get; } = [];

        public bool Truncated { get; set; }

        public Exception? Failure { get; set; }

        /// <summary>The agent's current history. Change it to simulate the agent starting a new one.</summary>
        public Guid HistoryId { get; set; } = Guid.NewGuid();

        /// <summary>When set, a request waits for it, so a test can act while a load is running.</summary>
        public TaskCompletionSource? Gate { get; set; }

        public async Task<PrinterEventPage> GetPrinterEventsAsync(Guid printerId, long? afterSequence, int limit, CancellationToken cancellationToken)
        {
            Requests.Add(afterSequence);
            if (Gate is { } gate)
            {
                await gate.Task.WaitAsync(cancellationToken);
            }

            if (Failure is not null)
            {
                throw Failure;
            }

            var latest = Stored.Count == 0 ? 0 : Stored[^1].Sequence;
            var events = afterSequence is null
                ? Stored.TakeLast(limit).ToList()
                : Stored.Where(e => e.Sequence > afterSequence).Take(limit).ToList();
            return new PrinterEventPage(events, latest, Truncated, HistoryId);
        }

        public IReadOnlyList<ConnectorDescriptor> Connectors => [];

        public Task DeleteFileAsync(Guid deviceId, string fileName, CancellationToken cancellationToken) => throw new NotSupportedException();

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

    private static PrinterEvent Event(long sequence, string message = "something") =>
        new(sequence, Start.AddMinutes(sequence), PrinterSeverity.Info, "test", $"{message} {sequence}");

    private static (PrinterEventsViewModel ViewModel, FakeCommands Commands, Guid PrinterId) Create(int storedEvents = 0)
    {
        var commands = new FakeCommands();
        for (var i = 1; i <= storedEvents; i++)
        {
            commands.Stored.Add(Event(i));
        }

        var printerId = Guid.NewGuid();
        return (new PrinterEventsViewModel(printerId, "Voron", commands), commands, printerId);
    }

    private static void Publish(Guid printerId, Guid historyId, PrinterEvent printerEvent) =>
        WeakReferenceMessenger.Default.Send(new PrinterEventData(new PrinterEventRecorded(printerId, historyId, printerEvent)), printerId);

    [Fact]
    public void Nothing_is_requested_until_the_history_is_opened()
    {
        var (_, commands, _) = Create(storedEvents: 3);

        Assert.Empty(commands.Requests);
    }

    [Fact]
    public async Task Loading_shows_the_newest_event_first()
    {
        var (viewModel, _, _) = Create(storedEvents: 3);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(["something 3", "something 2", "something 1"], viewModel.Events.Select(e => e.Message));
        Assert.False(viewModel.IsLoading);
        Assert.False(viewModel.IsEmpty);
    }

    [Fact]
    public async Task A_printer_with_no_history_says_so_instead_of_showing_a_blank_list()
    {
        var (viewModel, _, _) = Create();

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsEmpty);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task A_failed_load_shows_the_error_and_can_be_retried()
    {
        var (viewModel, commands, _) = Create(storedEvents: 1);
        commands.Failure = new InvalidOperationException("boom");

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasError);
        Assert.False(viewModel.IsEmpty);
        Assert.False(viewModel.IsLoading);

        commands.Failure = null;
        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasError);
        Assert.Single(viewModel.Events);
    }

    [Fact]
    public async Task A_history_the_agent_had_to_cut_is_marked_incomplete()
    {
        var (viewModel, commands, _) = Create(storedEvents: 2);
        commands.Truncated = true;

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsIncomplete);
    }

    [Fact]
    public async Task The_next_live_event_is_added_on_top_without_asking_the_agent()
    {
        var (viewModel, commands, printerId) = Create(storedEvents: 2);
        await viewModel.LoadCommand.ExecuteAsync(null);
        var requests = commands.Requests.Count;

        Publish(printerId, commands.HistoryId, Event(3, "live"));

        Assert.Equal("live 3", viewModel.Events[0].Message);
        Assert.Equal(3, viewModel.Events.Count);
        Assert.Equal(requests, commands.Requests.Count);
    }

    [Fact]
    public async Task A_live_event_seen_twice_is_shown_once()
    {
        var (viewModel, commands, printerId) = Create(storedEvents: 2);
        await viewModel.LoadCommand.ExecuteAsync(null);

        Publish(printerId, commands.HistoryId, Event(2));
        Publish(printerId, commands.HistoryId, Event(3));
        Publish(printerId, commands.HistoryId, Event(3));

        Assert.Equal(3, viewModel.Events.Count);
    }

    [Fact]
    public async Task A_live_event_that_skips_ahead_fetches_what_was_missed()
    {
        var (viewModel, commands, printerId) = Create(storedEvents: 2);
        await viewModel.LoadCommand.ExecuteAsync(null);
        commands.Stored.Add(Event(3));
        commands.Stored.Add(Event(4));

        Publish(printerId, commands.HistoryId, Event(4));

        // The fake answers synchronously, so the load the message started has finished by now.
        Assert.Equal([4L, 3, 2, 1], viewModel.Events.Select(e => e.Event.Sequence));
        Assert.Equal(2L, commands.Requests[^1]);
    }

    [Fact]
    public void A_live_event_before_the_first_load_is_ignored_because_the_load_will_include_it()
    {
        var (viewModel, commands, printerId) = Create(storedEvents: 1);

        Publish(printerId, commands.HistoryId, Event(1));

        Assert.Empty(viewModel.Events);
        Assert.Empty(commands.Requests);
    }

    [Fact]
    public async Task A_new_history_is_noticed_even_when_it_has_already_grown_past_the_old_cursor()
    {
        var (viewModel, commands, _) = Create(storedEvents: 5);
        await viewModel.LoadCommand.ExecuteAsync(null);

        // After the restart the agent recorded 7 events, so sequence 6 and 7 exist in both histories.
        commands.HistoryId = Guid.NewGuid();
        commands.Stored.Clear();
        for (var i = 1; i <= 7; i++)
        {
            commands.Stored.Add(Event(i, "new"));
        }

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(7, viewModel.Events.Count);
        Assert.All(viewModel.Events, e => Assert.StartsWith("new", e.Message));
    }

    [Fact]
    public async Task A_live_event_from_another_history_makes_the_dialog_start_over()
    {
        var (viewModel, commands, printerId) = Create(storedEvents: 3);
        await viewModel.LoadCommand.ExecuteAsync(null);
        commands.HistoryId = Guid.NewGuid();
        commands.Stored.Clear();
        commands.Stored.Add(Event(1, "new"));
        commands.Stored.Add(Event(2, "new"));
        commands.Stored.Add(Event(3, "new"));
        commands.Stored.Add(Event(4, "new"));

        // Sequence 4 follows our cursor of 3, so only the history id shows that it is not the same history.
        Publish(printerId, commands.HistoryId, Event(4, "new"));

        Assert.Equal(4, viewModel.Events.Count);
        Assert.All(viewModel.Events, e => Assert.StartsWith("new", e.Message));
    }

    [Fact]
    public void Nothing_is_said_to_be_empty_before_the_first_answer_is_in()
    {
        var (viewModel, _, _) = Create();

        Assert.False(viewModel.IsEmpty);
    }

    [Fact]
    public async Task A_live_event_during_a_load_makes_it_fetch_again_instead_of_cancelling_it()
    {
        var (viewModel, commands, printerId) = Create(storedEvents: 2);
        commands.Gate = new TaskCompletionSource();

        var load = viewModel.LoadCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsLoading);

        // An event is recorded while the first request is still waiting for its answer.
        commands.Stored.Add(Event(3, "live"));
        Publish(printerId, commands.HistoryId, Event(3, "live"));
        commands.Gate.SetResult();
        await load;

        Assert.False(viewModel.IsLoading);
        Assert.True(viewModel.IsLoaded);
        Assert.Equal(["live 3", "something 2", "something 1"], viewModel.Events.Select(e => e.Message));
    }

    [Fact]
    public async Task A_closed_dialog_ignores_what_the_agent_records_afterwards()
    {
        var (viewModel, commands, printerId) = Create(storedEvents: 1);
        await viewModel.LoadCommand.ExecuteAsync(null);
        var requests = commands.Requests.Count;

        viewModel.Stop();
        Publish(printerId, commands.HistoryId, Event(3));

        Assert.Single(viewModel.Events);
        Assert.Equal(requests, commands.Requests.Count);
    }

    [Fact]
    public async Task Only_the_newest_events_are_kept_on_screen()
    {
        var (viewModel, commands, printerId) = Create(storedEvents: 1);
        await viewModel.LoadCommand.ExecuteAsync(null);

        for (var i = 2; i <= PrinterEventsViewModel.MaxShown + 10; i++)
        {
            Publish(printerId, commands.HistoryId, Event(i));
        }

        Assert.Equal(PrinterEventsViewModel.MaxShown, viewModel.Events.Count);
        Assert.Equal(PrinterEventsViewModel.MaxShown + 10, viewModel.Events[0].Event.Sequence);
        Assert.True(viewModel.IsIncomplete);
    }
}
