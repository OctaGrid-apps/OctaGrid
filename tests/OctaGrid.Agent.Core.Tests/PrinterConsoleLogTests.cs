using OctaGrid.Agent.Core.Events;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Agent.Core.Tests;

public class PrinterConsoleLogTests
{
    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private static (PrinterConsoleLog Log, ManualTime Time, Guid PrinterId) Create(PrinterConsoleLogOptions? options = null)
    {
        var time = new ManualTime();
        return (new PrinterConsoleLog(time, options), time, Guid.NewGuid());
    }

    [Fact]
    public void Lines_are_recorded_in_order_with_increasing_sequences()
    {
        var (log, _, printer) = Create();
        var sink = log.CreateSink(printer);

        sink.WriteLine("first");
        sink.WriteLine("second");

        var page = log.Get(printer, null, 10);
        Assert.Equal(["first", "second"], page.Lines.Select(l => l.Text));
        Assert.Equal([1L, 2L], page.Lines.Select(l => l.Sequence));
        Assert.Equal(2, page.LatestSequence);
        Assert.NotEqual(Guid.Empty, page.StreamId);
    }

    [Fact]
    public void A_printer_that_wrote_nothing_has_an_empty_page_without_a_stream()
    {
        var (log, _, printer) = Create();

        var page = log.Get(printer, null, 10);

        Assert.Empty(page.Lines);
        Assert.Equal(0, page.LatestSequence);
        Assert.Equal(Guid.Empty, page.StreamId);
    }

    [Fact]
    public void Text_with_several_lines_becomes_several_entries_and_empty_lines_are_skipped()
    {
        var (log, _, printer) = Create();

        log.CreateSink(printer).WriteLine("M104 S210\r\n\r\nok\nT:210.0 /210.0\r");

        Assert.Equal(["M104 S210", "ok", "T:210.0 /210.0"], log.Get(printer, null, 10).Lines.Select(l => l.Text));
    }

    [Fact]
    public void Unicode_line_and_paragraph_separators_end_a_line_like_a_line_feed()
    {
        var (log, _, printer) = Create();

        log.CreateSink(printer).WriteLine("one\u2028two\u2029three\u0085four");

        Assert.Equal(["one", "two", "three", "four"], log.Get(printer, null, 10).Lines.Select(l => l.Text));
    }

    [Fact]
    public void Control_characters_are_replaced_and_trailing_space_is_trimmed()
    {
        var (log, _, printer) = Create();

        log.CreateSink(printer).WriteLine("a\tb\u001b[31m  ");

        Assert.Equal(["a b [31m"], log.Get(printer, null, 10).Lines.Select(l => l.Text));
    }

    [Fact]
    public void Blank_text_records_nothing()
    {
        var (log, _, printer) = Create();
        var sink = log.CreateSink(printer);

        sink.WriteLine("");
        sink.WriteLine("   \t ");
        sink.WriteLine(null!);

        Assert.Empty(log.Get(printer, null, 10).Lines);
    }

    [Fact]
    public void A_long_line_is_cut_and_ends_with_an_ellipsis()
    {
        var (log, _, printer) = Create(new PrinterConsoleLogOptions(MaxLineLength: 20));

        log.CreateSink(printer).WriteLine(new string('x', 100));

        var text = Assert.Single(log.Get(printer, null, 10).Lines).Text;
        Assert.Equal(20, text.Length);
        Assert.EndsWith("…", text);
    }

    [Fact]
    public void A_huge_write_is_bounded_before_it_is_processed_and_the_gap_is_reported_later()
    {
        var (log, time, printer) = Create();
        var sink = log.CreateSink(printer);
        var huge = string.Concat(Enumerable.Repeat("x\n", 2_500_000));

        sink.WriteLine(huge);

        // Only the burst got into the log, however many lines were handed over.
        Assert.Equal(200, log.Get(printer, null, 500).Lines.Count);

        time.Advance(TimeSpan.FromSeconds(10));
        sink.WriteLine("after");
        var marker = log.Get(printer, null, 500).Lines.Single(l => l.Text.Contains("skipped", StringComparison.Ordinal)).Text;
        var skipped = int.Parse(marker.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture);

        // What was counted is what fits in one write, not what was handed over.
        Assert.InRange(skipped, 1, new PrinterConsoleLogOptions().MaxWriteLength);
    }

    [Fact]
    public void Text_beyond_what_one_write_may_carry_is_not_looked_at_and_is_reported_as_skipped()
    {
        var (log, time, printer) = Create(new PrinterConsoleLogOptions(MaxWriteLength: 10));
        var sink = log.CreateSink(printer);

        sink.WriteLine("aaaa\nbbbb\ncccc\ndddd");
        time.Advance(TimeSpan.FromSeconds(1));
        sink.WriteLine("after");

        Assert.Equal(["aaaa", "bbbb", "… 1 lines skipped", "after"], log.Get(printer, null, 10).Lines.Select(l => l.Text));
    }

    [Fact]
    public void One_enormous_line_is_cut_without_being_copied_whole()
    {
        var (log, _, printer) = Create();

        log.CreateSink(printer).WriteLine(new string('y', 50_000_000));

        var line = Assert.Single(log.Get(printer, null, 10).Lines);
        Assert.Equal(new PrinterConsoleLogOptions().MaxLineLength, line.Text.Length);
        Assert.EndsWith("…", line.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_line_cut_at_the_limit_keeps_its_ellipsis_even_when_spaces_come_right_after_the_cut()
    {
        var (log, _, printer) = Create();
        var limit = new PrinterConsoleLogOptions().MaxLineLength;

        // Spaces start exactly where the limit falls, so trimming them would make the cut line look complete.
        log.CreateSink(printer).WriteLine(new string('x', limit - 1) + new string(' ', 5) + "tail");

        var text = Assert.Single(log.Get(printer, null, 10).Lines).Text;
        Assert.EndsWith("…", text, StringComparison.Ordinal);
        Assert.True(text.Length <= limit);
    }

    [Fact]
    public void An_echo_is_recorded_even_when_the_rate_limit_has_no_room_left()
    {
        var (log, _, printer) = Create(new PrinterConsoleLogOptions(Burst: 2, LinesPerSecond: 1));
        var sink = log.CreateSink(printer);
        for (var i = 0; i < 5; i++)
        {
            sink.WriteLine($"spam {i}");
        }

        log.Echo(printer, "> M112");

        Assert.Contains(log.Get(printer, null, 20).Lines, l => l.Text == "> M112");
    }

    [Fact]
    public void The_oldest_lines_are_dropped_when_the_log_is_full_and_the_page_says_so()
    {
        var (log, time, printer) = Create(new PrinterConsoleLogOptions(Capacity: 3));
        var sink = log.CreateSink(printer);

        for (var i = 1; i <= 5; i++)
        {
            sink.WriteLine($"line {i}");
            time.Advance(TimeSpan.FromSeconds(1));
        }

        var page = log.Get(printer, null, 10);
        Assert.Equal(["line 3", "line 4", "line 5"], page.Lines.Select(l => l.Text));
        Assert.True(page.Truncated);
        Assert.Equal(5, page.LatestSequence);
    }

    [Fact]
    public void A_cursor_returns_only_what_came_after_it()
    {
        var (log, time, printer) = Create();
        var sink = log.CreateSink(printer);
        for (var i = 1; i <= 4; i++)
        {
            sink.WriteLine($"line {i}");
            time.Advance(TimeSpan.FromSeconds(1));
        }

        var page = log.Get(printer, 2, 10);

        Assert.Equal(["line 3", "line 4"], page.Lines.Select(l => l.Text));
        Assert.False(page.Truncated);
    }

    [Fact]
    public void A_cursor_that_points_at_dropped_lines_is_reported_as_truncated()
    {
        var (log, time, printer) = Create(new PrinterConsoleLogOptions(Capacity: 2));
        var sink = log.CreateSink(printer);
        for (var i = 1; i <= 6; i++)
        {
            sink.WriteLine($"line {i}");
            time.Advance(TimeSpan.FromSeconds(1));
        }

        var page = log.Get(printer, 1, 10);

        Assert.True(page.Truncated);
        Assert.Equal(["line 5", "line 6"], page.Lines.Select(l => l.Text));
    }

    [Fact]
    public void Without_a_cursor_only_the_newest_lines_up_to_the_limit_are_returned()
    {
        var (log, time, printer) = Create();
        var sink = log.CreateSink(printer);
        for (var i = 1; i <= 5; i++)
        {
            sink.WriteLine($"line {i}");
            time.Advance(TimeSpan.FromSeconds(1));
        }

        var page = log.Get(printer, null, 2);

        Assert.Equal(["line 4", "line 5"], page.Lines.Select(l => l.Text));
        Assert.True(page.Truncated);
    }

    [Fact]
    public void A_flood_of_lines_is_limited_and_the_gap_is_reported_once_it_is_over()
    {
        var (log, time, printer) = Create(new PrinterConsoleLogOptions(Burst: 3, LinesPerSecond: 1));
        var sink = log.CreateSink(printer);

        for (var i = 1; i <= 10; i++)
        {
            sink.WriteLine($"line {i}");
        }

        // Only the burst got through; the rest was skipped.
        Assert.Equal(["line 1", "line 2", "line 3"], log.Get(printer, null, 50).Lines.Select(l => l.Text));

        time.Advance(TimeSpan.FromSeconds(2));
        sink.WriteLine("after");

        var texts = log.Get(printer, null, 50).Lines.Select(l => l.Text).ToList();
        Assert.Equal("… 7 lines skipped", texts[^2]);
        Assert.Equal("after", texts[^1]);
    }

    [Fact]
    public void Each_printer_has_its_own_log_and_stream()
    {
        var (log, _, first) = Create();
        var second = Guid.NewGuid();

        log.CreateSink(first).WriteLine("one");
        log.CreateSink(second).WriteLine("two");

        var firstPage = log.Get(first, null, 10);
        var secondPage = log.Get(second, null, 10);
        Assert.Equal(["one"], firstPage.Lines.Select(l => l.Text));
        Assert.Equal(["two"], secondPage.Lines.Select(l => l.Text));
        Assert.NotEqual(firstPage.StreamId, secondPage.StreamId);
    }

    [Fact]
    public void Recorded_is_raised_for_every_line_with_the_printer_and_stream()
    {
        var (log, _, printer) = Create();
        var notices = new List<PrinterConsoleLineRecorded>();
        log.Recorded += notices.Add;

        log.CreateSink(printer).WriteLine("a\nb");

        Assert.Equal(["a", "b"], notices.Select(n => n.Line.Text));
        Assert.All(notices, n => Assert.Equal(printer, n.PrinterId));
        Assert.All(notices, n => Assert.Equal(log.Get(printer, null, 10).StreamId, n.StreamId));
    }

    [Fact]
    public void A_handler_that_throws_does_not_stop_the_writer_or_other_handlers()
    {
        var (log, _, printer) = Create();
        var seen = new List<string>();
        log.Recorded += _ => throw new InvalidOperationException("boom");
        log.Recorded += n => seen.Add(n.Line.Text);

        log.CreateSink(printer).WriteLine("still works");

        Assert.Equal(["still works"], seen);
        Assert.Single(log.Get(printer, null, 10).Lines);
    }

    [Fact]
    public void Retain_forgets_the_logs_of_printers_that_are_gone()
    {
        var (log, _, kept) = Create();
        var gone = Guid.NewGuid();
        log.CreateSink(kept).WriteLine("keep");
        log.CreateSink(gone).WriteLine("drop");

        log.Retain([kept]);

        Assert.Single(log.Get(kept, null, 10).Lines);
        Assert.Empty(log.Get(gone, null, 10).Lines);
        Assert.Equal(Guid.Empty, log.Get(gone, null, 10).StreamId);
    }
}
