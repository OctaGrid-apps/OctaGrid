using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using OctaGrid.Client.Controls;
using OctaGrid.Client.Models;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Tests;

[Collection(UiCollection.Name)]
public class TemperatureHistoryTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = Start;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    /// <summary>A sample the agent stored <paramref name="second"/> seconds after <see cref="Start"/>.</summary>
    internal static TelemetrySample Sample(double second, double nozzle, double nozzleTarget = 0, double bed = 0, double bedTarget = 0) =>
        new(Start.AddSeconds(second), [
            new TelemetryReading(TelemetryReadingKeys.Nozzle, nozzle, nozzleTarget),
            new TelemetryReading(TelemetryReadingKeys.Bed, bed, bedTarget),
        ]);

    /// <summary>The value of one reading of a sample.</summary>
    internal static double Value(TelemetrySample sample, string key = TelemetryReadingKeys.Nozzle) =>
        sample.Readings.Single(r => r.Key == key).Value;

    [Fact]
    public void Add_keeps_samples_oldest_first()
    {
        var history = new TemperatureHistory();

        history.Add(Sample(0, 20));
        history.Add(Sample(1, 30, nozzleTarget: 200, bed: 25, bedTarget: 60));

        var samples = history.Snapshot();
        Assert.Equal([20.0, 30], samples.Select(s => Value(s)));
        Assert.Equal(Sample(1, 30, 200, 25, 60).Readings, samples[1].Readings);
    }

    [Fact]
    public void Add_drops_a_sample_with_a_time_it_already_has()
    {
        var history = new TemperatureHistory();
        var raised = 0;
        history.Changed += (_, _) => raised++;

        Assert.True(history.Add(Sample(4, 20)));
        Assert.True(history.Add(Sample(5, 21)));
        Assert.False(history.Add(Sample(5, 22)));
        Assert.False(history.Add(Sample(4, 23)));

        Assert.Equal([20.0, 21], history.Snapshot().Select(s => Value(s)));
        Assert.Equal(2, raised);
    }

    [Fact]
    public void After_the_agent_clock_steps_back_new_samples_replace_the_old_timeline_instead_of_being_dropped()
    {
        var history = new TemperatureHistory();
        foreach (var second in new[] { 0, 1, 2, 300, 301 })
        {
            history.Add(Sample(second, second));
        }

        Assert.True(history.Add(Sample(3, 3)));
        Assert.True(history.Add(Sample(4, 4)));

        Assert.Equal([0.0, 1, 2, 3, 4], history.Snapshot().Select(s => Value(s)));
        Assert.Equal(Start.AddSeconds(4), history.LastTime);
    }

    [Fact]
    public void Only_the_most_recent_samples_up_to_the_capacity_are_kept()
    {
        var history = new TemperatureHistory();

        for (var i = 0; i < TemperatureHistory.Capacity + 5; i++)
        {
            history.Add(Sample(i, i));
        }

        var samples = history.Snapshot();
        Assert.Equal(TemperatureHistory.Capacity, samples.Count);
        Assert.Equal(5, Value(samples[0]));
        Assert.Equal(TemperatureHistory.Capacity + 4, Value(samples[^1]));
    }

    [Fact]
    public void Merge_puts_the_history_before_live_samples_that_came_first_and_keeps_each_time_once()
    {
        var history = new TemperatureHistory();
        history.Add(Sample(10, 50));
        history.Add(Sample(11, 51));

        history.Merge([Sample(8, 48), Sample(9, 49), Sample(10, 50)]);

        Assert.Equal([48.0, 49, 50, 51], history.Snapshot().Select(s => Value(s)));
        Assert.Equal(Start.AddSeconds(11), history.LastTime);
    }

    [Fact]
    public void Snapshot_is_a_copy_that_later_samples_do_not_change()
    {
        var history = new TemperatureHistory();
        history.Add(Sample(0, 20));
        var before = history.Snapshot();

        history.Add(Sample(1, 30));

        Assert.Single(before);
        Assert.Null(new TemperatureHistory().LastTime);
    }

    [Fact]
    public void Series_are_the_known_keys_in_their_order_then_others_as_they_appear()
    {
        var samples = new[]
        {
            new TelemetrySample(Start, [new TelemetryReading("chamber", 30, null), new TelemetryReading(TelemetryReadingKeys.Bed, 60, 60)]),
            new TelemetrySample(Start.AddSeconds(1), [new TelemetryReading(TelemetryReadingKeys.Nozzle, 200, 210), new TelemetryReading("mcu", 40, null)]),
        };

        Assert.Equal([TelemetryReadingKeys.Nozzle, TelemetryReadingKeys.Bed, "chamber", "mcu"], TemperatureChart.SeriesKeys(samples));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(60, 100)]
    [InlineData(215, 250)]
    [InlineData(300, 350)]
    public void The_chart_ceiling_is_a_grid_multiple_above_every_reading_and_at_least_100(double highest, double expected)
    {
        Assert.Equal(expected, TemperatureChart.CeilingFor([Sample(0, highest)], hidden: null));
    }

    [Fact]
    public void A_hidden_series_does_not_raise_the_ceiling()
    {
        var samples = new[] { Sample(0, 215, nozzleTarget: 220, bed: 60, bedTarget: 60) };

        Assert.Equal(100, TemperatureChart.CeilingFor(samples, [TelemetryReadingKeys.Nozzle]));
    }

    [Fact]
    public void A_single_reading_is_held_until_the_right_edge_so_an_idle_printer_has_a_line()
    {
        var recorded = new[] { Sample(0, 24, bed: 24) };
        var end = Start.AddSeconds(30);

        var samples = TemperatureChart.WithLatestHeldUntil(recorded, end);

        Assert.Equal(2, samples.Count);
        Assert.Equal(24, Value(samples[1]));
        Assert.Equal(end, samples[1].Time);
    }

    [Fact]
    public void The_right_edge_moves_once_per_second_however_redraws_and_samples_fall_within_it()
    {
        var recorded = new List<TelemetrySample> { Sample(10.3, 20) };

        // Redraws from the clock and from new samples at different points of the same second agree on the edge.
        var atClockTick = TemperatureChart.AxisEnd(Start.AddSeconds(10.35), recorded);
        recorded.Add(Sample(10.9, 21));
        var atSample = TemperatureChart.AxisEnd(Start.AddSeconds(10.95), recorded);
        var nextSecond = TemperatureChart.AxisEnd(Start.AddSeconds(11.02), recorded);

        Assert.Equal(Start.AddSeconds(11), atClockTick);
        Assert.Equal(atClockTick, atSample);
        Assert.Equal(Start.AddSeconds(12), nextSecond);
    }

    [Fact]
    public void The_right_edge_follows_an_agent_clock_that_runs_ahead()
    {
        var recorded = new[] { Sample(14.2, 20) };

        Assert.Equal(Start.AddSeconds(15), TemperatureChart.AxisEnd(Start.AddSeconds(10.5), recorded));
        Assert.Equal(Start.AddSeconds(11), TemperatureChart.AxisEnd(Start.AddSeconds(10.5), []));
    }

    [Theory]
    [InlineData(0.0, 1020)]
    [InlineData(0.4, 620)]
    [InlineData(0.999, 21)]
    public void The_clock_ticks_just_after_the_next_whole_second(double second, double expectedMilliseconds)
    {
        var wait = TemperatureChart.UntilNextSecond(Start.AddSeconds(second));

        Assert.Equal(expectedMilliseconds, wait.TotalMilliseconds, precision: 3);
    }

    [Theory]
    [InlineData(0, 300)]
    [InlineData(95, 300)]
    [InlineData(400, 400)]
    [InlineData(4000, 1200)]
    public void The_time_axis_fits_the_readings_between_5_minutes_and_the_history_window(double seconds, double expected)
    {
        var samples = new[] { Sample(0, 0), Sample(seconds, 0) };

        Assert.Equal(TimeSpan.FromSeconds(expected), TemperatureChart.SpanFor(samples));
    }

    [Theory]
    [InlineData(30, "30 s")]
    [InlineData(119, "119 s")]
    [InlineData(120, "2 min")]
    [InlineData(1200, "20 min")]
    public void The_time_axis_label_uses_seconds_below_two_minutes_and_minutes_above(double seconds, string expected)
    {
        Assert.Equal(expected, TemperatureChart.FormatSpan(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(4, 0)]
    [InlineData(6, 1)]
    [InlineData(10, 1)]
    [InlineData(29, 2)]
    [InlineData(99, 2)]
    public void The_read_out_shows_the_sample_closest_to_the_pointer(double second, int expected)
    {
        var samples = new[] { Sample(0, 0), Sample(10, 0), Sample(30, 0) };

        Assert.Equal(expected, TemperatureChart.NearestIndex(samples, Start.AddSeconds(second)));
    }

    [Fact]
    public void The_read_out_lists_the_time_and_each_visible_series_with_its_target_when_one_is_set()
    {
        var sample = Sample(0, 200.44, nozzleTarget: 210, bed: 59.96, bedTarget: 0);
        var keys = new[] { TelemetryReadingKeys.Nozzle, TelemetryReadingKeys.Bed };

        // The read-out follows the user's culture; the expected text is written with a decimal point.
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            var all = TemperatureChart.ReadOutLines(sample, keys, hidden: null);
            var withoutBed = TemperatureChart.ReadOutLines(sample, keys, [TelemetryReadingKeys.Bed]);

            Assert.Equal(["Hotend: 200.4° → 210°", "Bed: 60.0°"], all.Skip(1));
            Assert.Equal(["Hotend: 200.4° → 210°"], withoutBed.Skip(1));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void Nothing_is_held_before_the_first_reading()
    {
        Assert.Empty(TemperatureChart.WithLatestHeldUntil([], Start));
    }

    [Fact]
    public async Task The_chart_draws_with_hidden_series_and_with_the_pointer_over_it()
    {
        await HeadlessUi.Run(() =>
        {
            var time = new ManualTime();
            var history = new TemperatureHistory(time);
            var chart = new TemperatureChart
            {
                Width = 400,
                Height = 200,
                Source = history,
                GridBrush = Brushes.Gray,
                TextBrush = Brushes.Black,
                ReadOutBackground = Brushes.White,
            };
            var window = new Window { Content = chart, Width = 500, Height = 300 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            for (var i = 0; i < 5; i++)
            {
                history.Add(Sample(i, 20 + i * 40, 215, 20 + i * 10, 60));
                time.Advance(TimeSpan.FromSeconds(1));
            }

            chart.HiddenKeys = [TelemetryReadingKeys.Bed];
            window.MouseMove(new Point(300, 100));
            Dispatcher.UIThread.RunJobs();
            window.MouseMove(new Point(490, 290));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(5, history.Snapshot().Count);
            window.Close();
        });
    }
}
