using System.Globalization;
using Avalonia.Data.Converters;

namespace OctaGrid.Client.Converters;

public sealed class SecondsToReadableTimeConverter : IValueConverter
{
    public static readonly SecondsToReadableTimeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int or long or double or float)
        {
            var totalSeconds = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
            var time = TimeSpan.FromSeconds(totalSeconds);

            if (time.TotalSeconds <= 0)
            {
                return "0m";
            }

            List<string> parts = [];

            if (time.Days > 0)
            {
                parts.Add($"{time.Days}d");
            }

            if (time.Hours > 0)
            {
                parts.Add($"{time.Hours}h");
            }

            if (time.Minutes > 0)
            {
                parts.Add($"{time.Minutes}m");
            }

            if (time.TotalMinutes < 1 && time.Seconds > 0)
            {
                parts.Add($"{time.Seconds}s");
            }

            return string.Join(" ", parts);
        }

        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
