using OctaGrid.Client.Models;
using OctaGrid.Client.Services;
using OctaGrid.Client.ViewModels;
using OctaGrid.Protocol.Messaging;
using static OctaGrid.Client.Tests.TemperatureHistoryTests;

namespace OctaGrid.Client.Tests;

public class TemperatureChartViewModelTests
{
    [Fact]
    public void The_legend_lists_the_hotend_and_bed_then_other_readings_the_history_has()
    {
        var history = new TemperatureHistory();
        history.Add(new TelemetrySample(Sample(0, 0).Time, [new TelemetryReading("chamber", 30, null)]));

        var chart = new TemperatureChartViewModel(history, new InMemorySettingsStore());

        Assert.Equal(["Hotend", "Bed", "chamber"], chart.Series.Select(s => s.Label));
        Assert.All(chart.Series, s => Assert.True(s.IsVisible));
        Assert.Empty(chart.HiddenKeys);
    }

    [Fact]
    public void Series_the_user_hid_before_start_out_hidden()
    {
        var settings = new InMemorySettingsStore(new ClientSettings { HiddenChartSeries = [TelemetryReadingKeys.Bed] });

        var chart = new TemperatureChartViewModel(new TemperatureHistory(), settings);

        Assert.False(chart.Series.Single(s => s.Key == TelemetryReadingKeys.Bed).IsVisible);
        Assert.Equal([TelemetryReadingKeys.Bed], chart.HiddenKeys);
    }

    [Fact]
    public void Hiding_a_series_hides_it_from_the_chart_and_is_remembered_without_losing_other_choices()
    {
        var settings = new InMemorySettingsStore(new ClientSettings { HiddenChartSeries = ["chamber"] });
        var chart = new TemperatureChartViewModel(new TemperatureHistory(), settings);
        var before = chart.HiddenKeys;

        chart.Series.Single(s => s.Key == TelemetryReadingKeys.Nozzle).IsVisible = false;

        Assert.NotSame(before, chart.HiddenKeys);
        Assert.Equal([TelemetryReadingKeys.Nozzle], chart.HiddenKeys);
        Assert.Equal(["chamber", TelemetryReadingKeys.Nozzle], settings.ClientSettings.HiddenChartSeries);

        chart.Series.Single(s => s.Key == TelemetryReadingKeys.Nozzle).IsVisible = true;

        Assert.Empty(chart.HiddenKeys);
        Assert.Equal(["chamber"], settings.ClientSettings.HiddenChartSeries);
    }
}
