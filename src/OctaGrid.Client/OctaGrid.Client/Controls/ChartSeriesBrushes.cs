using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace OctaGrid.Client.Controls;

/// <summary>
/// The colour of a chart series, from the theme: <c>Chart.Series.&lt;key&gt;</c> for keys with their own colour, otherwise
/// one of <c>Chart.Palette.0</c> to <c>Chart.Palette.3</c>, chosen by the key so a series keeps its colour.
/// </summary>
public static class ChartSeriesBrushes
{
    private const int PaletteSize = 4;

    /// <summary>The brush of a series, or null when the theme defines none.</summary>
    public static IBrush? For(string key)
    {
        var app = Application.Current;
        if (app is null)
        {
            return null;
        }

        var theme = app.ActualThemeVariant;
        if (app.TryFindResource($"Chart.Series.{key}", theme, out var own) && own is IBrush brush)
        {
            return brush;
        }

        // A stable hash: string.GetHashCode changes between runs, and the colour must not.
        var index = key.Aggregate(0, (hash, c) => (hash * 31 + c) & 0x7FFFFFFF) % PaletteSize;
        return app.TryFindResource($"Chart.Palette.{index}", theme, out var fallback) ? fallback as IBrush : null;
    }

    /// <summary>For the legend: turns a reading key into its series brush.</summary>
    public static IValueConverter Converter { get; } = new KeyConverter();

    private sealed class KeyConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is string key ? For(key) ?? AvaloniaProperty.UnsetValue : AvaloniaProperty.UnsetValue;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
