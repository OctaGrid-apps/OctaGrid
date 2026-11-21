using OctaGrid.Agent.Core.Link;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Agent.Core.Tests;

/// <summary>A state change is reported once per change, with the state before it.</summary>
public sealed class PrinterStateTrackerTests
{
    private static readonly Guid PrinterId = Guid.NewGuid();

    private readonly FixedTime _time = new();

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    }
    private readonly PrinterStateTracker _tracker;

    public PrinterStateTrackerTests()
    {
        _tracker = new PrinterStateTracker(_time);
    }

    private static PrinterTelemetry Snapshot(UniversalPrinterState state, string? fileName = null) =>
        new(PrinterId) { State = state, PrintStatus = new PrintStatus { FileName = fileName } };

    [Fact]
    public void The_first_state_seen_is_a_change_without_a_previous_state()
    {
        var changed = _tracker.Observe(Snapshot(UniversalPrinterState.Printing, "benchy.gcode"));

        Assert.NotNull(changed);
        Assert.Null(changed.Previous);
        Assert.Equal(UniversalPrinterState.Printing, changed.Current);
        Assert.Equal("benchy.gcode", changed.FileName);
        Assert.Equal(_time.GetUtcNow(), changed.OccurredAt);
    }

    [Fact]
    public void A_snapshot_in_the_same_state_is_not_a_change()
    {
        _tracker.Observe(Snapshot(UniversalPrinterState.Printing));

        Assert.Null(_tracker.Observe(Snapshot(UniversalPrinterState.Printing)));
    }

    [Fact]
    public void A_new_state_is_reported_with_the_one_before_it()
    {
        _tracker.Observe(Snapshot(UniversalPrinterState.Printing));

        var changed = _tracker.Observe(Snapshot(UniversalPrinterState.Completed));

        Assert.NotNull(changed);
        Assert.Equal(UniversalPrinterState.Printing, changed.Previous);
        Assert.Equal(UniversalPrinterState.Completed, changed.Current);
    }

    [Fact]
    public void A_printer_the_agent_no_longer_runs_starts_again_without_a_previous_state()
    {
        _tracker.Observe(Snapshot(UniversalPrinterState.Printing));
        _tracker.Retain([]);

        var changed = _tracker.Observe(Snapshot(UniversalPrinterState.Printing));

        Assert.NotNull(changed);
        Assert.Null(changed.Previous);
    }
}
