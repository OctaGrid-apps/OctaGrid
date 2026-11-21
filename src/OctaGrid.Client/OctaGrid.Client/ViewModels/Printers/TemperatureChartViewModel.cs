using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;

namespace OctaGrid.Client.ViewModels;

/// <summary>One line of the temperature chart, as its legend shows it: a toggle that hides or shows the line.</summary>
public partial class TemperatureSeriesViewModel : ViewModelBase
{
    public TemperatureSeriesViewModel(string key, bool isVisible)
    {
        Key = key;
        Label = ReadingLabels.For(key);
        IsVisible = isVisible;
    }

    /// <summary>The reading key, which also picks the line's colour.</summary>
    public string Key { get; }

    public string Label { get; }

    [ObservableProperty]
    public partial bool IsVisible { get; set; }
}

/// <summary>
/// The temperature chart on the printer page with its legend. Which lines are hidden is the user's choice for every
/// printer, so it is kept in the client settings and restored the next time a chart is shown.
/// </summary>
public partial class TemperatureChartViewModel : ViewModelBase
{
    private readonly IAppSettingsStore _settings;

    public TemperatureChartViewModel(TemperatureHistory history, IAppSettingsStore settings)
    {
        History = history;
        _settings = settings;

        var hidden = settings.ClientSettings.HiddenChartSeries.ToHashSet(StringComparer.Ordinal);
        var keys = ReadingLabels.KnownOrder
            .Concat(history.Snapshot().SelectMany(s => s.Readings).Select(r => r.Key))
            .Distinct(StringComparer.Ordinal);
        Series = keys.Select(key => new TemperatureSeriesViewModel(key, !hidden.Contains(key))).ToList();
        foreach (var series in Series)
        {
            series.PropertyChanged += OnSeriesChanged;
        }

        HiddenKeys = HiddenNow();
    }

    /// <summary>The readings the chart draws.</summary>
    public TemperatureHistory History { get; }

    /// <summary>
    /// The legend: the readings every printer reports, then any other the history has when the page opens. The chart
    /// itself draws every reading that is not hidden.
    /// </summary>
    public IReadOnlyList<TemperatureSeriesViewModel> Series { get; }

    /// <summary>The keys the chart leaves out. A new set on every change, so the chart redraws.</summary>
    [ObservableProperty]
    public partial IReadOnlyCollection<string> HiddenKeys { get; private set; }

    private IReadOnlyCollection<string> HiddenNow() =>
        Series.Where(s => !s.IsVisible).Select(s => s.Key).ToHashSet(StringComparer.Ordinal);

    private void OnSeriesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TemperatureSeriesViewModel.IsVisible))
        {
            return;
        }

        HiddenKeys = HiddenNow();

        // Keys this legend does not list (hidden on a printer that reports more) stay as the user left them.
        var listed = Series.Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        _settings.Update(s => s with
        {
            HiddenChartSeries = s.HiddenChartSeries.Where(k => !listed.Contains(k)).Concat(HiddenKeys).Order(StringComparer.Ordinal).ToList(),
        });
    }
}
