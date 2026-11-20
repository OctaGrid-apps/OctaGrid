using Avalonia.Controls;
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

    [Fact]
    public void Add_keeps_samples_oldest_first_with_both_sensors()
    {
        var history = new TemperatureHistory();

        history.Add(Sample(0, 20));
        history.Add(Sample(1, 30, nozzleTarget: 200, bed: 25, bedTarget: 60));

        var samples = history.Snapshot();
        Assert.Equal([20, 30], samples.Select(s => s.Nozzle));
        Assert.Equal((200, 25, 60), (samples[1].NozzleTarget, samples[1].Bed, samples[1].BedTarget));
        Assert.Equal(Start.AddSeconds(1), samples[1].Time);
    }

    [Fact]
    public void Add_drops_a_sample_that_is_not_later_than_the_newest_one()
    {
        var history = new TemperatureHistory();
        var raised = 0;
        history.Changed += (_, _) => raised++;

        Assert.True(history.Add(Sample(5, 20)));
        Assert.False(history.Add(Sample(5, 21)));
        Assert.False(history.Add(Sample(4, 22)));

        Assert.Equal([20.0], history.Snapshot().Select(s => s.Nozzle));
        Assert.Equal(1, raised);
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
        Assert.Equal(5, samples[0].Nozzle);
        Assert.Equal(TemperatureHistory.Capacity + 4, samples[^1].Nozzle);
    }

    [Fact]
    public void Merge_puts_the_history_before_live_samples_that_came_first_and_keeps_each_time_once()
    {
        var history = new TemperatureHistory();
        history.Add(Sample(10, 50));
        history.Add(Sample(11, 51));

        history.Merge([Sample(8, 48), Sample(9, 49), Sample(10, 50)]);

        Assert.Equal([48.0, 49, 50, 51], history.Snapshot().Select(s => s.Nozzle));
        Assert.Equal(Start.AddSeconds(11), history.LastTime);
    }

    [Fact]
    public void A_sensor_the_sample_does_not_carry_is_drawn_at_zero()
    {
        var history = new TemperatureHistory();

        history.Add(new TelemetrySample(Start, [new TelemetryReading(TelemetryReadingKeys.Bed, 60, null)]));

        var sample = Assert.Single(history.Snapshot());
        Assert.Equal((0, 0, 60, 0), (sample.Nozzle, sample.NozzleTarget, sample.Bed, sample.BedTarget));
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

    [Theory]
    [InlineData(0, 100)]
    [InlineData(60, 100)]
    [InlineData(215, 250)]
    [InlineData(300, 350)]
    public void The_chart_ceiling_is_a_grid_multiple_above_every_reading_and_at_least_100(double highest, double expected)
    {
        var samples = new[] { new TemperatureSample(DateTimeOffset.UnixEpoch, highest, 0, 0, 0) };

        Assert.Equal(expected, TemperatureChart.CeilingFor(samples));
    }

    [Fact]
    public void A_single_reading_is_held_until_now_so_an_idle_printer_has_a_line()
    {
        var time = new ManualTime();
        var history = new TemperatureHistory(time);
        history.Add(Sample(0, 24, bed: 24));
        time.Advance(TimeSpan.FromSeconds(30));

        var samples = TemperatureChart.WithLatestHeldUntilNow(history);

        Assert.Equal(2, samples.Count);
        Assert.Equal(24, samples[1].Nozzle);
        Assert.Equal(history.Now, samples[1].Time);
        Assert.Single(history.Snapshot());
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(10, 30)]
    [InlineData(95, 95)]
    [InlineData(4000, 1200)]
    public void The_time_axis_fits_the_readings_between_30_seconds_and_the_history_window(double seconds, double expected)
    {
        var start = DateTimeOffset.UnixEpoch;
        var samples = new[]
        {
            new TemperatureSample(start, 0, 0, 0, 0),
            new TemperatureSample(start + TimeSpan.FromSeconds(seconds), 0, 0, 0, 0),
        };

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

    [Fact]
    public void Nothing_is_held_before_the_first_reading()
    {
        Assert.Empty(TemperatureChart.WithLatestHeldUntilNow(new TemperatureHistory(new ManualTime())));
        Assert.Empty(TemperatureChart.WithLatestHeldUntilNow(null));
    }

    [Fact]
    public async Task The_chart_draws_with_and_without_readings()
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
                BedBrush = Brushes.Blue,
                HotendBrush = Brushes.Red,
                GridBrush = Brushes.Gray,
                TextBrush = Brushes.Black,
            };
            var window = new Window { Content = chart, Width = 500, Height = 300 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            for (var i = 0; i < 5; i++)
            {
                history.Add(Sample(i, 20 + i * 40, 215, 20 + i * 10, 60));
                time.Advance(TimeSpan.FromSeconds(1));
            }

            Dispatcher.UIThread.RunJobs();
            chart.InvalidateVisual();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(5, history.Snapshot().Count);
            window.Close();
        });
    }
}
