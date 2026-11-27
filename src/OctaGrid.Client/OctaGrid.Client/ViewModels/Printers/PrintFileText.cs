using System.Globalization;
using OctaGrid.Client.Converters;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Client.ViewModels;

/// <summary>Formats a printer file's details for lists, in the current culture.</summary>
public static class PrintFileText
{
    private const string Separator = " • ";

    /// <summary>Shown in place of a file's details while they are read from the printer.</summary>
    public const string ReadingDetails = "Reading details…";
    private static readonly string[] SizeUnits = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>Material, nozzle and filament weight, for example "PLA • 0.4 mm • 12.5 g".</summary>
    public static string Details(PrintMetadata file)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(file.MaterialType))
        {
            parts.Add(file.MaterialType);
        }

        if (file.NozzleDiameter is { } nozzle)
        {
            parts.Add($"{nozzle.ToString("0.##", CultureInfo.CurrentCulture)} mm");
        }

        if (Weight(file.FilamentWeightGrams) is { } weight)
        {
            parts.Add(weight);
        }

        return string.Join(Separator, parts);
    }

    /// <summary>Details, estimated time, size and date on one line.</summary>
    public static string Summary(PrintMetadata file)
    {
        var parts = new List<string>(4);
        if (Details(file) is { Length: > 0 } details)
        {
            parts.Add(details);
        }

        if (SecondsToReadableTimeConverter.Instance.Convert(file.EstimatedTimeSeconds, typeof(string), null, CultureInfo.CurrentCulture) is string time)
        {
            parts.Add(time);
        }

        parts.Add(Size(file.FileSize));
        if (Date(file.ModifiedAt) is { } date)
        {
            parts.Add(date);
        }

        return string.Join(Separator, parts);
    }

    /// <summary>A filament weight, for example "12.5 g"; null when unknown.</summary>
    public static string? Weight(double? grams) =>
        grams is > 0 ? $"{grams.Value.ToString(grams.Value < 100 ? "0.#" : "0", CultureInfo.CurrentCulture)} g" : null;

    /// <summary>A size in bytes in binary units, for example "4.2 MB".</summary>
    public static string Size(long bytes)
    {
        double value = Math.Max(bytes, 0);
        var unit = 0;
        while (value >= 1024 && unit < SizeUnits.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var format = unit == 0 || value >= 100 ? "0" : "0.#";
        return $"{value.ToString(format, CultureInfo.CurrentCulture)} {SizeUnits[unit]}";
    }

    /// <summary>The day a file was written, as a short date; some printers report no time.</summary>
    public static string? Date(DateTimeOffset? modifiedAt)
    {
        if (modifiedAt is not { } at)
        {
            return null;
        }

        // Date-only values arrive as midnight UTC. Converting them to local time would show
        // the previous day west of UTC.
        var day = at.Offset == TimeSpan.Zero && at.TimeOfDay == TimeSpan.Zero ? at.DateTime : at.ToLocalTime().DateTime;
        return day.ToString("d", CultureInfo.CurrentCulture);
    }
}
