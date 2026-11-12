using OctaGrid.Agent.Core.Events;
using OctaGrid.Extensions.Models;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Agent.Core.Tests;

public class PrinterEventLogTests
{
    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private static readonly Guid Printer = Guid.NewGuid();

    private static (PrinterEventLog Log, ManualTimeProvider Time) Create(PrinterEventLogOptions? options = null)
    {
        var time = new ManualTimeProvider();
        return (new PrinterEventLog(time, options), time);
    }

    private static PrinterTelemetry Snapshot(
        UniversalPrinterState state = UniversalPrinterState.Idle, params PrinterCondition[] conditions) =>
        new(Printer) { State = state, Conditions = conditions };

    private static PrinterCondition Condition(string code, string message, PrinterSeverity severity = PrinterSeverity.Error) =>
        new(code, severity, message);

    [Fact]
    public void Events_get_increasing_sequences_and_the_time_they_were_recorded()
    {
        var (log, time) = Create();

        var first = log.Record(Printer, PrinterSeverity.Info, "a", "first");
        time.Advance(TimeSpan.FromSeconds(1));
        var second = log.Record(Printer, PrinterSeverity.Info, "a", "second");

        Assert.Equal(1, first!.Sequence);
        Assert.Equal(2, second!.Sequence);
        Assert.Equal(TimeSpan.FromSeconds(1), second.Timestamp - first.Timestamp);
    }

    [Fact]
    public void Without_a_cursor_the_newest_events_come_back_oldest_first()
    {
        var (log, _) = Create();
        for (var i = 1; i <= 5; i++)
        {
            log.Record(Printer, PrinterSeverity.Info, "e", $"event {i}");
        }

        var page = log.Get(Printer, null, 3);

        Assert.Equal([3L, 4, 5], page.Events.Select(e => e.Sequence));
        Assert.Equal(5, page.LatestSequence);
        Assert.True(page.Truncated); // events 1 and 2 are kept, but are not in this page
    }

    [Fact]
    public void Without_a_cursor_a_page_that_reaches_the_first_event_is_not_truncated()
    {
        var (log, _) = Create();
        for (var i = 1; i <= 3; i++)
        {
            log.Record(Printer, PrinterSeverity.Info, "e", $"event {i}");
        }

        Assert.False(log.Get(Printer, null, 3).Truncated);
        Assert.False(log.Get(Printer, null, 50).Truncated);
    }

    [Fact]
    public void With_a_cursor_only_later_events_come_back()
    {
        var (log, _) = Create();
        for (var i = 1; i <= 5; i++)
        {
            log.Record(Printer, PrinterSeverity.Info, "e", $"event {i}");
        }

        var page = log.Get(Printer, 3, 10);

        Assert.Equal([4L, 5], page.Events.Select(e => e.Sequence));
        Assert.False(page.Truncated);
    }

    [Fact]
    public void A_page_smaller_than_what_is_waiting_lets_the_caller_continue()
    {
        var (log, _) = Create();
        for (var i = 1; i <= 5; i++)
        {
            log.Record(Printer, PrinterSeverity.Info, "e", $"event {i}");
        }

        var page = log.Get(Printer, 0, 2);

        Assert.Equal([1L, 2], page.Events.Select(e => e.Sequence));
        Assert.Equal(5, page.LatestSequence);
    }

    [Fact]
    public void Only_the_newest_events_up_to_the_capacity_are_kept()
    {
        var (log, _) = Create(new PrinterEventLogOptions(Capacity: 3, Burst: 100));
        for (var i = 1; i <= 10; i++)
        {
            log.Record(Printer, PrinterSeverity.Info, "e", $"event {i}");
        }

        var page = log.Get(Printer, null, 100);

        Assert.Equal([8L, 9, 10], page.Events.Select(e => e.Sequence));
        Assert.True(page.Truncated);
    }

    [Fact]
    public void A_caller_that_fell_behind_the_capacity_is_told_events_were_lost()
    {
        var (log, _) = Create(new PrinterEventLogOptions(Capacity: 3, Burst: 100));
        for (var i = 1; i <= 10; i++)
        {
            log.Record(Printer, PrinterSeverity.Info, "e", $"event {i}");
        }

        Assert.True(log.Get(Printer, 2, 100).Truncated);
        Assert.False(log.Get(Printer, 7, 100).Truncated);
    }

    [Fact]
    public void An_unknown_printer_has_an_empty_history()
    {
        var (log, _) = Create();

        var page = log.Get(Guid.NewGuid(), null, 10);

        Assert.Empty(page.Events);
        Assert.Equal(0, page.LatestSequence);
        Assert.False(page.Truncated);
    }

    [Fact]
    public void Printers_keep_separate_histories()
    {
        var (log, _) = Create();
        var other = Guid.NewGuid();

        log.Record(Printer, PrinterSeverity.Info, "e", "mine");
        log.Record(other, PrinterSeverity.Info, "e", "theirs");

        Assert.Equal("mine", Assert.Single(log.Get(Printer, null, 10).Events).Message);
        Assert.Equal(1, log.Get(other, null, 10).Events[0].Sequence);
    }

    [Fact]
    public void An_event_that_repeats_the_previous_one_is_left_out()
    {
        var (log, _) = Create();

        log.Record(Printer, PrinterSeverity.Error, "x", "same");
        var repeat = log.Record(Printer, PrinterSeverity.Error, "x", "same");

        Assert.Null(repeat);
        Assert.Single(log.Get(Printer, null, 10).Events);
    }

    [Fact]
    public void Long_messages_and_codes_are_cut()
    {
        var (log, _) = Create(new PrinterEventLogOptions(MaxMessageLength: 10, MaxCodeLength: 5));

        var recorded = log.Record(Printer, PrinterSeverity.Info, new string('c', 50), new string('m', 50));

        Assert.Equal(5, recorded!.Code.Length);
        Assert.Equal(10, recorded.Message.Length);
        Assert.EndsWith("…", recorded.Message);
    }

    [Fact]
    public void Blank_events_are_ignored()
    {
        var (log, _) = Create();

        Assert.Null(log.Record(Printer, PrinterSeverity.Info, "e", "  "));
        Assert.Null(log.Record(Printer, PrinterSeverity.Info, "", "text"));
        Assert.Empty(log.Get(Printer, null, 10).Events);
    }

    [Fact]
    public void A_flood_is_limited_and_summarised_once_the_printer_calms_down()
    {
        var (log, time) = Create(new PrinterEventLogOptions(Burst: 3, RefillInterval: TimeSpan.FromSeconds(10)));

        for (var i = 1; i <= 10; i++)
        {
            log.Record(Printer, PrinterSeverity.Info, "e", $"event {i}");
        }

        Assert.Equal(3, log.Get(Printer, null, 100).Events.Count);

        time.Advance(TimeSpan.FromSeconds(10));
        log.Record(Printer, PrinterSeverity.Info, "e", "after the flood");

        var events = log.Get(Printer, null, 100).Events;
        Assert.Equal(["event 1", "event 2", "event 3"], events.Take(3).Select(e => e.Message));
        Assert.Equal("events.suppressed", events[3].Code);
        Assert.Contains("7 events", events[3].Message);
        Assert.Equal("after the flood", events[4].Message);
    }

    [Fact]
    public void A_noisy_printer_cannot_use_up_the_burst_that_warnings_and_errors_need()
    {
        var (log, _) = Create(new PrinterEventLogOptions(Burst: 2, RefillInterval: TimeSpan.FromHours(1)));

        for (var i = 1; i <= 10; i++)
        {
            log.Record(Printer, PrinterSeverity.Info, "noise", $"noise {i}");
        }

        var warning = log.Record(Printer, PrinterSeverity.Warning, "w", "a warning");
        var error = log.Record(Printer, PrinterSeverity.Error, "e", "an error");

        Assert.NotNull(warning);
        Assert.NotNull(error);

        // The loss is reported as soon as something is recorded again, here by the warning that came first.
        var messages = log.Get(Printer, null, 50).Events.Select(e => e.Message).ToList();
        Assert.Contains(messages, m => m.Contains("8 events were skipped", StringComparison.Ordinal));
        Assert.Contains("an error", messages);
    }

    [Fact]
    public void The_first_snapshot_of_an_offline_printer_records_nothing()
    {
        var (log, _) = Create();

        log.Observe(Snapshot(UniversalPrinterState.Offline));

        Assert.Empty(log.Get(Printer, null, 10).Events);
    }

    [Fact]
    public void A_state_change_is_recorded_once()
    {
        var (log, _) = Create();

        log.Observe(Snapshot(UniversalPrinterState.Printing));
        log.Observe(Snapshot(UniversalPrinterState.Printing));
        log.Observe(Snapshot(UniversalPrinterState.Paused));

        var events = log.Get(Printer, null, 10).Events;
        Assert.Equal(["State changed to Printing.", "State changed to Paused."], events.Select(e => e.Message));
        Assert.All(events, e => Assert.Equal("state.changed", e.Code));
    }

    [Theory]
    [InlineData(UniversalPrinterState.Error, PrinterSeverity.Error)]
    [InlineData(UniversalPrinterState.Failed, PrinterSeverity.Warning)]
    [InlineData(UniversalPrinterState.Printing, PrinterSeverity.Info)]
    public void A_state_change_is_as_serious_as_the_state(UniversalPrinterState state, PrinterSeverity expected)
    {
        var (log, _) = Create();

        log.Observe(Snapshot(state));

        Assert.Equal(expected, Assert.Single(log.Get(Printer, null, 10).Events).Severity);
    }

    [Fact]
    public void A_condition_is_recorded_when_it_appears_and_again_when_it_is_resolved()
    {
        var (log, _) = Create();
        var condition = Condition("connection.failed", "Cannot connect");

        log.Observe(Snapshot(UniversalPrinterState.Offline, condition));
        log.Observe(Snapshot(UniversalPrinterState.Offline, condition));
        log.Observe(Snapshot(UniversalPrinterState.Idle));

        var events = log.Get(Printer, null, 10).Events;
        Assert.Equal(["Cannot connect", "State changed to Idle.", "Resolved: Cannot connect"], events.Select(e => e.Message));
        Assert.Equal(PrinterSeverity.Error, events[0].Severity);
        Assert.Equal(PrinterSeverity.Info, events[2].Severity);
    }

    [Fact]
    public void While_offline_a_missing_condition_is_unknown_and_is_resolved_only_when_the_printer_is_back()
    {
        var (log, _) = Create();

        log.Observe(Snapshot(UniversalPrinterState.Printing, Condition("filament.low", "Low", PrinterSeverity.Warning)));
        log.Observe(Snapshot(UniversalPrinterState.Offline, Condition("connection.failed", "Cannot connect")));
        log.Observe(Snapshot(UniversalPrinterState.Offline));

        var whileOffline = log.Get(Printer, null, 20).Events.Select(e => e.Message).ToList();
        Assert.Contains("Cannot connect", whileOffline);
        Assert.DoesNotContain(whileOffline, m => m.StartsWith("Resolved", StringComparison.Ordinal));

        log.Observe(Snapshot(UniversalPrinterState.Idle));

        var afterwards = log.Get(Printer, null, 20).Events.Select(e => e.Message).ToList();
        Assert.Contains("Resolved: Low", afterwards);
        Assert.Contains("Resolved: Cannot connect", afterwards);
    }

    [Fact]
    public void What_is_remembered_while_offline_stays_bounded_whatever_the_connector_sends()
    {
        var (log, _) = Create(new PrinterEventLogOptions(MaxConditions: 3));

        for (var i = 1; i <= 20; i++)
        {
            log.Observe(Snapshot(UniversalPrinterState.Offline, Condition($"code{i}", $"Fault {i}")));
        }

        log.Observe(Snapshot(UniversalPrinterState.Idle));

        var resolved = log.Get(Printer, null, 100).Events.Where(e => e.Message.StartsWith("Resolved", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, resolved.Count);
        Assert.Contains(resolved, e => e.Message == "Resolved: Fault 20"); // what the last snapshot reported is kept
    }

    [Fact]
    public void The_end_of_a_recorded_fault_is_never_lost_to_the_rate_limit()
    {
        var (log, _) = Create(new PrinterEventLogOptions(Burst: 2, RefillInterval: TimeSpan.FromHours(1)));
        for (var i = 1; i <= 10; i++)
        {
            log.Record(Printer, PrinterSeverity.Info, "noise", $"noise {i}");
        }

        log.Observe(Snapshot(UniversalPrinterState.Offline, Condition("fault", "Heater failed")));
        log.Observe(Snapshot(UniversalPrinterState.Idle));

        var messages = log.Get(Printer, null, 100).Events.Select(e => e.Message).ToList();
        Assert.Contains("Heater failed", messages);
        Assert.Contains("Resolved: Heater failed", messages);
    }

    [Fact]
    public void Sanitize_returns_the_same_snapshot_when_nothing_needs_changing()
    {
        var (log, _) = Create();
        var snapshot = Snapshot(UniversalPrinterState.Idle, Condition("a", "Fine"), Condition("b", "Also fine"));

        Assert.Same(snapshot, log.Sanitize(snapshot));
    }

    [Fact]
    public void Sanitize_trims_cuts_and_deduplicates_so_events_and_sent_state_agree()
    {
        var (log, _) = Create(new PrinterEventLogOptions(MaxCodeLength: 6, MaxMessageLength: 40));
        var snapshot = Snapshot(UniversalPrinterState.Idle,
            Condition(" filament.low ", "  Low  ", PrinterSeverity.Warning),
            Condition("filament.low", "Low (again)", PrinterSeverity.Error),
            Condition("abcdefghij", "x"));

        var cleaned = log.Sanitize(snapshot);

        Assert.Equal(["filam…", "abcde…"], cleaned.Conditions.Select(c => c.Code));
        Assert.Equal("Low (again)", cleaned.Conditions[0].Message); // the most serious of the duplicates is kept
        log.Observe(cleaned);
        Assert.Equal(["Low (again)", "x"], log.Get(Printer, null, 10).Events.Select(e => e.Message).Where(m => !m.StartsWith("State changed", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_snapshot_that_changes_nothing_records_and_announces_nothing()
    {
        var (log, _) = Create();
        var announced = 0;
        log.Observe(Snapshot(UniversalPrinterState.Printing, Condition("c", "Careful", PrinterSeverity.Warning)));
        log.Recorded += _ => announced++;

        for (var i = 0; i < 50; i++)
        {
            log.Observe(Snapshot(UniversalPrinterState.Printing, Condition("c", "Careful", PrinterSeverity.Warning)));
        }

        Assert.Equal(0, announced);
        Assert.Equal(2, log.Get(Printer, null, 10).LatestSequence);
    }

    [Fact]
    public void A_condition_whose_message_changes_is_recorded_again()
    {
        var (log, _) = Create();

        log.Observe(Snapshot(UniversalPrinterState.Offline, Condition("connection.failed", "Timed out")));
        log.Observe(Snapshot(UniversalPrinterState.Offline, Condition("connection.failed", "Refused")));

        Assert.Equal(["Timed out", "Refused"], log.Get(Printer, null, 10).Events.Select(e => e.Message));
    }

    [Fact]
    public void Every_recorded_event_is_announced_with_its_printer_including_ones_derived_from_snapshots()
    {
        var (log, _) = Create();
        var announced = new List<PrinterEventRecorded>();
        log.Recorded += announced.Add;

        log.Observe(Snapshot(UniversalPrinterState.Printing, Condition("c", "Careful", PrinterSeverity.Warning)));
        log.Record(Printer, PrinterSeverity.Info, "x", "explicit");

        var page = log.Get(Printer, null, 10);
        Assert.All(announced, a => Assert.Equal(Printer, a.PrinterId));
        Assert.All(announced, a => Assert.Equal(page.HistoryId, a.HistoryId));
        Assert.Equal([1L, 2, 3], announced.Select(a => a.Event.Sequence));
        Assert.Equal(page.Events, announced.Select(a => a.Event));
    }

    [Fact]
    public void A_history_has_its_own_id_that_changes_when_the_printer_is_forgotten_and_comes_back()
    {
        var (log, _) = Create();
        Assert.Equal(Guid.Empty, log.Get(Printer, null, 10).HistoryId);

        log.Record(Printer, PrinterSeverity.Info, "x", "first");
        var before = log.Get(Printer, null, 10).HistoryId;
        Assert.NotEqual(Guid.Empty, before);
        Assert.Equal(before, log.Get(Printer, null, 10).HistoryId);

        log.Retain([]);
        log.Record(Printer, PrinterSeverity.Info, "x", "again");

        Assert.NotEqual(before, log.Get(Printer, null, 10).HistoryId);
    }

    [Fact]
    public void A_snapshot_with_a_missing_condition_list_or_entry_is_handled()
    {
        var (log, _) = Create();

        log.Observe(new PrinterTelemetry(Printer) { State = UniversalPrinterState.Printing, Conditions = null! });
        log.Observe(new PrinterTelemetry(Printer) { State = UniversalPrinterState.Paused, Conditions = [null!, Condition("ok", "Fine")] });

        Assert.Equal(
            ["State changed to Printing.", "State changed to Paused.", "Fine"],
            log.Get(Printer, null, 10).Events.Select(e => e.Message));
    }

    [Fact]
    public void An_event_that_was_left_out_is_not_announced()
    {
        var (log, _) = Create();
        var count = 0;
        log.Recorded += _ => count++;

        log.Record(Printer, PrinterSeverity.Info, "x", "same");
        log.Record(Printer, PrinterSeverity.Info, "x", "same");

        Assert.Equal(1, count);
    }

    [Fact]
    public void A_handler_that_throws_does_not_stop_the_others_or_the_reporter()
    {
        var (log, _) = Create();
        var reached = false;
        log.Recorded += _ => throw new InvalidOperationException("broken subscriber");
        log.Recorded += _ => reached = true;

        var recorded = log.Record(Printer, PrinterSeverity.Info, "x", "event");

        Assert.NotNull(recorded);
        Assert.True(reached);
    }

    [Fact]
    public void Retain_forgets_printers_that_are_no_longer_known()
    {
        var (log, _) = Create();
        var kept = Guid.NewGuid();
        log.Record(Printer, PrinterSeverity.Info, "e", "gone");
        log.Record(kept, PrinterSeverity.Info, "e", "stays");

        log.Retain([kept]);

        Assert.Empty(log.Get(Printer, null, 10).Events);
        Assert.Single(log.Get(kept, null, 10).Events);
    }
}
