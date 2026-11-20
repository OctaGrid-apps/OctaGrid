using OctaGrid.Agent.Core.Events;
using OctaGrid.Extensions.Models;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Agent.Core.Tests;

public class PrinterTelemetryLogTests
{
    private static readonly Guid Printer = Guid.NewGuid();

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private static (PrinterTelemetryLog Log, ManualTime Time) Create(PrinterTelemetryLogOptions? options = null)
    {
        var time = new ManualTime();
        return (new PrinterTelemetryLog(time, options), time);
    }

    private static PrinterTelemetry Heating(double? nozzle = null, double? nozzleTarget = null, double? bed = null, double? bedTarget = null) =>
        new(Printer)
        {
            State = UniversalPrinterState.Idle,
            NozzleTemperature = nozzle,
            NozzleTargetTemperature = nozzleTarget,
            BedTemperature = bed,
            BedTargetTemperature = bedTarget,
        };

    [Fact]
    public void A_snapshot_becomes_a_sample_with_a_reading_per_sensor_at_the_agent_time()
    {
        var (log, time) = Create();

        var sample = log.Record(Heating(nozzle: 200.5, nozzleTarget: 210, bed: 59.9, bedTarget: 60));

        Assert.NotNull(sample);
        Assert.Equal(time.GetUtcNow(), sample.Time);
        Assert.Equal(
            [new TelemetryReading(TelemetryReadingKeys.Nozzle, 200.5, 210), new TelemetryReading(TelemetryReadingKeys.Bed, 59.9, 60)],
            sample.Readings);
        Assert.Equal([sample], log.Get(Printer, null).History);
    }

    [Fact]
    public void A_partial_snapshot_is_completed_from_the_last_known_values()
    {
        var (log, time) = Create();
        log.Record(Heating(nozzle: 25, nozzleTarget: 0, bed: 24, bedTarget: 0));
        time.Advance(TimeSpan.FromSeconds(1));

        var sample = log.Record(Heating(nozzle: 30));

        Assert.Equal(
            [new TelemetryReading(TelemetryReadingKeys.Nozzle, 30, 0), new TelemetryReading(TelemetryReadingKeys.Bed, 24, 0)],
            sample!.Readings);
    }

    [Fact]
    public void A_snapshot_too_soon_after_the_last_sample_is_not_stored_but_still_updates_the_next_one()
    {
        var (log, time) = Create();
        log.Record(Heating(nozzle: 25, bed: 24));
        time.Advance(TimeSpan.FromMilliseconds(400));

        Assert.Null(log.Record(Heating(bed: 40)));

        time.Advance(TimeSpan.FromMilliseconds(600));
        var sample = log.Record(Heating(nozzle: 26));

        // Two samples: the first one, and the one a second later that also carries the bed value from in between.
        Assert.Equal([25.0, 24.0, 26.0, 40.0], log.Get(Printer, null).History.SelectMany(s => s.Readings).Select(r => r.Value));
        Assert.Same(sample, log.Get(Printer, null).History[^1]);
    }

    [Fact]
    public void A_snapshot_without_readings_stores_nothing()
    {
        var (log, _) = Create();

        Assert.Null(log.Record(new PrinterTelemetry(Printer) { State = UniversalPrinterState.Printing }));
        Assert.Null(log.Record(Heating(nozzleTarget: 210)));
        Assert.Empty(log.Get(Printer, null).History);
    }

    [Fact]
    public void A_printer_that_went_offline_does_not_carry_old_readings_into_new_samples()
    {
        var (log, time) = Create();
        log.Record(Heating(nozzle: 210, bed: 60));
        log.Record(new PrinterTelemetry(Printer) { State = UniversalPrinterState.Offline });
        time.Advance(TimeSpan.FromSeconds(5));

        var sample = log.Record(Heating(bed: 30));

        Assert.Equal([new TelemetryReading(TelemetryReadingKeys.Bed, 30, null)], sample!.Readings);
    }

    [Fact]
    public void Only_the_newest_samples_up_to_the_capacity_are_kept()
    {
        var (log, time) = Create(new PrinterTelemetryLogOptions(Capacity: 3));

        for (var i = 1; i <= 5; i++)
        {
            log.Record(Heating(nozzle: i));
            time.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal([3.0, 4.0, 5.0], log.Get(Printer, null).History.Select(s => s.Readings[0].Value));
    }

    [Fact]
    public void Get_after_a_time_returns_only_later_samples()
    {
        var (log, time) = Create();
        var first = log.Record(Heating(nozzle: 1))!;
        time.Advance(TimeSpan.FromSeconds(1));
        var second = log.Record(Heating(nozzle: 2))!;

        Assert.Equal([second], log.Get(Printer, first.Time).History);
        Assert.Empty(log.Get(Printer, second.Time).History);
        Assert.Empty(log.Get(Guid.NewGuid(), null).History);
    }

    [Fact]
    public void A_clock_that_steps_backwards_does_not_stop_recording()
    {
        var (log, time) = Create();
        log.Record(Heating(nozzle: 1));
        time.Advance(TimeSpan.FromMinutes(-5));

        Assert.NotNull(log.Record(Heating(nozzle: 2)));
        Assert.Equal(2, log.Get(Printer, null).History.Count);
    }

    [Fact]
    public void Retain_forgets_printers_that_are_no_longer_run()
    {
        var (log, _) = Create();
        var other = Guid.NewGuid();
        log.Record(Heating(nozzle: 1));
        log.Record(new PrinterTelemetry(other) { NozzleTemperature = 1 });

        log.Retain([other]);

        Assert.Empty(log.Get(Printer, null).History);
        Assert.Single(log.Get(other, null).History);
    }

    [Fact]
    public void The_backfill_holds_the_last_snapshot_even_when_it_added_no_sample()
    {
        var (log, _) = Create();
        log.Record(Heating(nozzle: 25));
        var paused = new PrinterTelemetry(Printer) { State = UniversalPrinterState.Paused };

        log.Record(paused);

        var backfill = log.Get(Printer, null);
        Assert.Same(paused, backfill.Current);
        Assert.Single(backfill.History);
        Assert.Equal(UniversalPrinterState.Offline, log.Get(Guid.NewGuid(), null).Current.State);
    }
}
