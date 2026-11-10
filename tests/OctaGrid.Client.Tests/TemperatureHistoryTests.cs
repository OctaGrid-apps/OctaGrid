using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using OctaGrid.Client.Controls;
using OctaGrid.Client.Models;

namespace OctaGrid.Client.Tests;

[Collection(UiCollection.Name)]
public class TemperatureHistoryTests
{
    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    [Fact]
    public void Add_keeps_readings_oldest_first()
    {
        var time = new ManualTime();
        var history = new TemperatureHistory(time);

        history.Add(20, 0, 20, 0);
        time.Advance(TimeSpan.FromSeconds(1));
        history.Add(30, 200, 25, 60);

        var samples = history.Snapshot();
        Assert.Equal([20, 30], samples.Select(s => s.Nozzle));
        Assert.Equal(200, samples[1].NozzleTarget);
        Assert.Equal(60, samples[1].BedTarget);
    }

    [Fact]
    public void Add_drops_a_reading_that_comes_too_soon_after_the_previous_one()
    {
        var time = new ManualTime();
        var history = new TemperatureHistory(time);

        Assert.True(history.Add(20, 0, 20, 0));
        time.Advance(TimeSpan.FromMilliseconds(400));
        Assert.False(history.Add(21, 0, 20, 0));

        Assert.Single(history.Snapshot());
    }

    [Fact]
    public void Add_keeps_only_the_most_recent_readings_up_to_the_capacity()
    {
        var time = new ManualTime();
        var history = new TemperatureHistory(time);

        for (var i = 0; i < TemperatureHistory.Capacity + 5; i++)
        {
            history.Add(i, 0, 0, 0);
            time.Advance(TemperatureHistory.MinInterval);
        }

        var samples = history.Snapshot();
        Assert.Equal(TemperatureHistory.Capacity, samples.Count);
        Assert.Equal(5, samples[0].Nozzle);
        Assert.Equal(TemperatureHistory.Capacity + 4, samples[^1].Nozzle);
    }

    [Fact]
    public void Add_raises_Changed_only_when_a_reading_was_kept()
    {
        var time = new ManualTime();
        var history = new TemperatureHistory(time);
        var raised = 0;
        history.Changed += (_, _) => raised++;

        history.Add(20, 0, 20, 0);
        history.Add(21, 0, 20, 0);

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Snapshot_is_a_copy_that_later_readings_do_not_change()
    {
        var time = new ManualTime();
        var history = new TemperatureHistory(time);
        history.Add(20, 0, 20, 0);
        var before = history.Snapshot();

        time.Advance(TemperatureHistory.MinInterval);
        history.Add(30, 0, 20, 0);

        Assert.Single(before);
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
        history.Add(24, 0, 24, 0);
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
    [InlineData(4000, 600)]
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
    [InlineData(600, "10 min")]
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
                history.Add(20 + i * 40, 215, 20 + i * 10, 60);
                time.Advance(TemperatureHistory.MinInterval);
            }

            Dispatcher.UIThread.RunJobs();
            chart.InvalidateVisual();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(5, history.Snapshot().Count);
            window.Close();
        });
    }
}
