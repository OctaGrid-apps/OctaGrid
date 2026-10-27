using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace OctaGrid.Client.Converters;

public sealed class StateBrushConverter : IValueConverter
{
    public static readonly StateBrushConverter Background = new(nameof(TextBlock.Background));
    public static readonly StateBrushConverter Foreground = new(nameof(TextBlock.Foreground));

    private readonly string _part;

    private StateBrushConverter(string part) => _part = part;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var app = Application.Current;
        if (app is null)
        {
            return AvaloniaProperty.UnsetValue;
        }

        var theme = app.ActualThemeVariant;
        if (value is not null &&
            app.TryFindResource($"State.{value}.{_part}", theme, out var brush))
        {
            return brush;
        }

        return app.TryFindResource($"State.Default.{_part}", theme, out var fallback)
            ? fallback
            : AvaloniaProperty.UnsetValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
