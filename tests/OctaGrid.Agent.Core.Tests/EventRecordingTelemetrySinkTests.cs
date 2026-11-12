using Microsoft.Extensions.Logging;
using OctaGrid.Agent.Core.Events;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Agent.Core.Tests;

public class EventRecordingTelemetrySinkTests
{
    private static readonly Guid Printer = Guid.NewGuid();

    [Fact]
    public void A_snapshot_is_recorded_and_passed_on_unchanged()
    {
        var inner = new FakeTelemetry();
        var log = new PrinterEventLog();
        var sink = new EventRecordingTelemetrySink(inner, log);
        var snapshot = new PrinterTelemetry(Printer) { State = UniversalPrinterState.Printing };

        sink.Send(snapshot);

        Assert.Same(snapshot, Assert.Single(inner.Sent));
        Assert.Equal("State changed to Printing.", Assert.Single(log.Get(Printer, null, 10).Events).Message);
    }

    [Fact]
    public void Too_many_or_too_long_conditions_are_cut_before_they_reach_the_host()
    {
        var inner = new FakeTelemetry();
        var log = new PrinterEventLog(options: new PrinterEventLogOptions(MaxConditions: 2, MaxMessageLength: 8));
        var sink = new EventRecordingTelemetrySink(inner, log);

        sink.Send(new PrinterTelemetry(Printer)
        {
            Conditions =
            [
                new PrinterCondition("a", PrinterSeverity.Info, "info"),
                new PrinterCondition("b", PrinterSeverity.Error, "a very long message"),
                new PrinterCondition("c", PrinterSeverity.Warning, "warning")
            ]
        });

        var forwarded = Assert.Single(inner.Sent).Conditions;
        Assert.Equal(["b", "c"], forwarded.Select(c => c.Code));
        Assert.All(forwarded, c => Assert.True(c.Message.Length <= 8));
    }

    [Fact]
    public void The_events_and_the_state_sent_on_are_made_from_the_same_conditions()
    {
        var inner = new FakeTelemetry();
        var log = new PrinterEventLog();
        var sink = new EventRecordingTelemetrySink(inner, log);

        sink.Send(new PrinterTelemetry(Printer)
        {
            Conditions = [new PrinterCondition("  filament.low ", PrinterSeverity.Warning, " Low ")]
        });

        var forwarded = Assert.Single(Assert.Single(inner.Sent).Conditions);
        var recorded = Assert.Single(log.Get(Printer, null, 10).Events);
        Assert.Equal(forwarded.Code, recorded.Code);
        Assert.Equal(forwarded.Message, recorded.Message);
    }

    [Fact]
    public void Missing_conditions_from_a_careless_extension_are_dropped_and_the_snapshot_still_reaches_the_host()
    {
        var inner = new FakeTelemetry();
        var sink = new EventRecordingTelemetrySink(inner, new PrinterEventLog());

        sink.Send(new PrinterTelemetry(Printer) { State = UniversalPrinterState.Printing, Conditions = null! });
        sink.Send(new PrinterTelemetry(Printer)
        {
            Conditions = [new PrinterCondition("x", PrinterSeverity.Error, null!), new PrinterCondition("ok", PrinterSeverity.Info, "fine")]
        });

        Assert.Equal(UniversalPrinterState.Printing, inner.Sent[0].State);
        Assert.Empty(inner.Sent[0].Conditions);
        Assert.Equal(["ok"], inner.Sent[1].Conditions.Select(c => c.Code));
    }

    private sealed class SwitchableTimeProvider : TimeProvider
    {
        public bool Broken { get; set; }

        public override DateTimeOffset GetUtcNow() =>
            Broken ? throw new InvalidOperationException("clock broken") : DateTimeOffset.UnixEpoch;
    }

    private sealed class LevelLogger : ILogger<EventRecordingTelemetrySink>
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Levels.Add(logLevel);
    }

    [Fact]
    public void A_recording_fault_never_stops_the_snapshot_and_is_logged_in_full_once_until_recording_works_again()
    {
        // Creating a printer's history or recording an event asks for the time, so a broken clock makes recording fail.
        var clock = new SwitchableTimeProvider { Broken = true };
        var logger = new LevelLogger();
        var inner = new FakeTelemetry();
        var sink = new EventRecordingTelemetrySink(inner, new PrinterEventLog(clock), logger);

        sink.Send(new PrinterTelemetry(Printer) { State = UniversalPrinterState.Printing });
        sink.Send(new PrinterTelemetry(Printer) { State = UniversalPrinterState.Printing });
        clock.Broken = false;
        sink.Send(new PrinterTelemetry(Printer) { State = UniversalPrinterState.Printing });
        clock.Broken = true;
        sink.Send(new PrinterTelemetry(Printer) { State = UniversalPrinterState.Paused });

        Assert.Equal([LogLevel.Error, LogLevel.Debug, LogLevel.Error], logger.Levels);
        Assert.Equal(4, inner.Sent.Count);
    }
}
