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
    internal sealed class FakeCommands : IAgentCommandsService
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

        public List<string> Sent { get; } = [];

        public Exception? SendFailure { get; set; }

        /// <summary>A command in here is accepted but does not finish until its gate is opened: a printer busy with it.</summary>
        public Dictionary<string, TaskCompletionSource> Busy { get; } = [];

        public async Task SendConsoleCommandAsync(Guid printerId, string command, CancellationToken cancellationToken)
        {
            if (SendFailure is not null)
            {
                throw SendFailure;
            }

            Sent.Add(command);
            if (Busy.TryGetValue(command, out var gate))
            {
                await gate.Task.WaitAsync(cancellationToken);
            }
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

        public Task<ConnectionTestResult> TestConnectionAsync(string connectorId, IReadOnlyDictionary<string, string> settings, Guid? printerId, CancellationToken cancellationToken) => throw new NotSupportedException();

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
    public async Task Loading_shows_the_newest_line_first()
    {
        var (viewModel, _, _) = Create(storedLines: 3);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(["line 3", "line 2", "line 1"], viewModel.Lines.Select(l => l.Text));
        Assert.False(viewModel.IsLoading);
        Assert.False(viewModel.IsEmpty);
    }

    [Fact]
    public async Task The_text_to_copy_is_oldest_first_with_the_time_of_each_line()
    {
        var (viewModel, _, _) = Create(storedLines: 3);
        await viewModel.LoadCommand.ExecuteAsync(null);

        var lines = viewModel.GetTranscript().Split(Environment.NewLine);

        Assert.Equal(3, lines.Length);
        Assert.EndsWith(" line 1", lines[0], StringComparison.Ordinal);
        Assert.EndsWith(" line 2", lines[1], StringComparison.Ordinal);
        Assert.EndsWith(" line 3", lines[2], StringComparison.Ordinal);
        Assert.StartsWith(viewModel.Lines[2].TimeText, lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void There_is_no_text_to_copy_before_anything_was_written()
    {
        Assert.Equal(string.Empty, Create().ViewModel.GetTranscript());
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
    public async Task The_next_live_line_is_added_at_the_top_without_asking_the_agent()
    {
        var (viewModel, commands, printerId) = Create(storedLines: 2);
        await viewModel.LoadCommand.ExecuteAsync(null);
        var requests = commands.Requests.Count;

        Publish(printerId, commands.StreamId, Line(3, "live"));

        Assert.Equal("live 3", viewModel.Lines[0].Text);
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

        Assert.Equal(["line 4", "line 3", "line 2", "line 1"], viewModel.Lines.Select(l => l.Text));
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

        Assert.Equal(["fresh 5", "fresh 4", "fresh 3", "fresh 2", "fresh 1"], viewModel.Lines.Select(l => l.Text));
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
        Assert.Equal($"line {PrinterConsoleViewModel.MaxShown + 5}", viewModel.Lines[0].Text);
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

    private static (PrinterConsoleViewModel ViewModel, FakeCommands Commands) CreateWithCommandBox(bool supports = true, bool expert = true)
    {
        var commands = new FakeCommands();
        var viewModel = new PrinterConsoleViewModel(Guid.NewGuid(), commands, supportsCommands: supports) { IsExpertMode = expert };
        return (viewModel, commands);
    }

    [Theory]
    [InlineData(true, true, true, false)]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, false, false)]
    public void The_command_box_needs_a_connector_that_takes_commands_and_Expert_mode(
        bool supports, bool expert, bool canSend, bool showHint)
    {
        var (viewModel, _) = CreateWithCommandBox(supports, expert);

        Assert.Equal(canSend, viewModel.CanSend);
        Assert.Equal(showHint, viewModel.ShowExpertModeHint);
    }

    [Fact]
    public async Task A_command_is_sent_without_the_spaces_around_it_and_the_box_is_emptied()
    {
        var (viewModel, commands) = CreateWithCommandBox();
        viewModel.CommandText = "  M105  ";

        await viewModel.SendCommand.ExecuteAsync(null);

        Assert.Equal(["M105"], commands.Sent);
        Assert.Equal(string.Empty, viewModel.CommandText);
        Assert.False(viewModel.HasSendError);
    }

    [Fact]
    public void Nothing_can_be_sent_while_the_box_is_empty_or_Expert_mode_is_off()
    {
        var (viewModel, _) = CreateWithCommandBox();
        Assert.False(viewModel.SendCommand.CanExecute(null));

        viewModel.CommandText = "M105";
        Assert.True(viewModel.SendCommand.CanExecute(null));

        viewModel.IsExpertMode = false;
        Assert.False(viewModel.SendCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_command_that_breaks_the_rules_is_refused_before_it_reaches_the_agent()
    {
        var (viewModel, commands) = CreateWithCommandBox();
        viewModel.CommandText = new string('x', Protocol.Printers.ConsoleCommandRules.MaxLength + 1);

        await viewModel.SendCommand.ExecuteAsync(null);

        Assert.Empty(commands.Sent);
        Assert.True(viewModel.HasSendError);
        Assert.NotEqual(string.Empty, viewModel.CommandText);
    }

    [Fact]
    public async Task A_reason_from_the_printer_is_shown_and_the_command_stays_for_a_fix()
    {
        var (viewModel, commands) = CreateWithCommandBox();
        commands.SendFailure = new Protocol.Exceptions.ValidationException("command", "Unknown command \"FOO\".");
        viewModel.CommandText = "FOO";

        await viewModel.SendCommand.ExecuteAsync(null);

        Assert.Equal("Unknown command \"FOO\".", viewModel.SendError);
        Assert.Equal("FOO", viewModel.CommandText);
    }

    [Fact]
    public async Task A_failure_nobody_expected_shows_a_general_message()
    {
        var (viewModel, commands) = CreateWithCommandBox();
        commands.SendFailure = new InvalidOperationException("boom");
        viewModel.CommandText = "M105";

        await viewModel.SendCommand.ExecuteAsync(null);

        Assert.Equal("The command could not be sent.", viewModel.SendError);
        Assert.DoesNotContain("boom", viewModel.SendError);
    }

    [Fact]
    public async Task The_next_successful_command_clears_the_earlier_error()
    {
        var (viewModel, commands) = CreateWithCommandBox();
        commands.SendFailure = new Protocol.Exceptions.ValidationException("command", "nope");
        viewModel.CommandText = "FOO";
        await viewModel.SendCommand.ExecuteAsync(null);
        Assert.True(viewModel.HasSendError);

        commands.SendFailure = null;
        viewModel.CommandText = "M105";
        await viewModel.SendCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasSendError);
    }

    [Theory]
    [InlineData("FIRMWARE_RESTART")]
    [InlineData("RESTART")]
    [InlineData("SAVE_CONFIG")]
    [InlineData("M502")]
    [InlineData("M112")]
    public async Task Expert_mode_is_the_confirmation_so_no_command_is_held_back_for_a_question(string command)
    {
        var (viewModel, commands) = CreateWithCommandBox();
        viewModel.CommandText = command;

        await viewModel.SendCommand.ExecuteAsync(null);

        Assert.Equal([command], commands.Sent);
    }

    [Fact]
    public async Task An_emergency_stop_can_be_sent_while_a_long_command_is_still_running()
    {
        var (viewModel, commands) = CreateWithCommandBox();
        var homing = new TaskCompletionSource();
        commands.Busy["G28"] = homing;
        viewModel.CommandText = "G28";

        var first = viewModel.SendCommand.ExecuteAsync(null);

        // The first command is still on its way, yet the box is free and the next command can go.
        Assert.False(first.IsCompleted);
        Assert.Equal(string.Empty, viewModel.CommandText);
        viewModel.CommandText = "M112";
        Assert.True(viewModel.SendCommand.CanExecute(null));
        await viewModel.SendCommand.ExecuteAsync(null);

        Assert.Equal(["G28", "M112"], commands.Sent);
        Assert.False(first.IsCompleted);

        homing.SetResult();
        await first;
    }

    [Fact]
    public async Task A_refused_command_is_given_back_only_when_the_user_has_not_started_something_else()
    {
        var (viewModel, commands) = CreateWithCommandBox();
        var gate = new TaskCompletionSource();
        commands.Busy["G28"] = gate;
        viewModel.CommandText = "G28";
        var pending = viewModel.SendCommand.ExecuteAsync(null);
        viewModel.CommandText = "M105";

        // The printer refuses G28 later, while M105 is already typed: it must not be overwritten.
        gate.SetException(new Protocol.Exceptions.ValidationException("command", "refused"));
        await pending;

        Assert.Equal("refused", viewModel.SendError);
        Assert.Equal("M105", viewModel.CommandText);
    }

    [Fact]
    public async Task A_command_the_printer_refused_is_not_remembered_for_recall()
    {
        var (viewModel, commands) = CreateWithCommandBox();
        viewModel.CommandText = "M105";
        await viewModel.SendCommand.ExecuteAsync(null);
        commands.SendFailure = new Protocol.Exceptions.ValidationException("command", "refused");
        viewModel.CommandText = "M587 P\"secret\"";
        await viewModel.SendCommand.ExecuteAsync(null);
        viewModel.CommandText = string.Empty;

        viewModel.RecallPreviousCommand.Execute(null);

        Assert.Equal("M105", viewModel.CommandText);
    }

    [Fact]
    public async Task Earlier_commands_can_be_recalled_newest_first_and_the_box_empties_past_the_newest()
    {
        var (viewModel, _) = CreateWithCommandBox();
        foreach (var command in new[] { "M105", "G28", "M115" })
        {
            viewModel.CommandText = command;
            await viewModel.SendCommand.ExecuteAsync(null);
        }

        viewModel.RecallPreviousCommand.Execute(null);
        Assert.Equal("M115", viewModel.CommandText);
        viewModel.RecallPreviousCommand.Execute(null);
        Assert.Equal("G28", viewModel.CommandText);
        viewModel.RecallPreviousCommand.Execute(null);
        viewModel.RecallPreviousCommand.Execute(null);
        Assert.Equal("M105", viewModel.CommandText);

        viewModel.RecallNextCommand.Execute(null);
        Assert.Equal("G28", viewModel.CommandText);
        viewModel.RecallNextCommand.Execute(null);
        viewModel.RecallNextCommand.Execute(null);
        Assert.Equal(string.Empty, viewModel.CommandText);
    }

    [Fact]
    public void Recalling_with_nothing_sent_changes_nothing()
    {
        var (viewModel, _) = CreateWithCommandBox();
        viewModel.CommandText = "partly typed";

        viewModel.RecallPreviousCommand.Execute(null);

        Assert.Equal("partly typed", viewModel.CommandText);
    }

    [Fact]
    public async Task Sending_the_same_command_again_is_remembered_once_and_only_the_latest_are_kept()
    {
        var (viewModel, _) = CreateWithCommandBox();
        foreach (var command in new[] { "M105", "M105" })
        {
            viewModel.CommandText = command;
            await viewModel.SendCommand.ExecuteAsync(null);
        }

        viewModel.RecallPreviousCommand.Execute(null);
        viewModel.RecallPreviousCommand.Execute(null);
        Assert.Equal("M105", viewModel.CommandText);

        for (var i = 0; i < PrinterConsoleViewModel.MaxRemembered + 5; i++)
        {
            viewModel.CommandText = $"M117 {i}";
            await viewModel.SendCommand.ExecuteAsync(null);
        }

        for (var i = 0; i < PrinterConsoleViewModel.MaxRemembered + 10; i++)
        {
            viewModel.RecallPreviousCommand.Execute(null);
        }

        Assert.Equal("M117 5", viewModel.CommandText);
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
