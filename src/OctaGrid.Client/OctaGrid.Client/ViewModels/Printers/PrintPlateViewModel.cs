using System.Globalization;
using OctaGrid.Client.Converters;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Client.ViewModels;

/// <summary>A plate of a file in the start dialog.</summary>
public sealed class PrintPlateViewModel
{
    public PrintPlateViewModel(PrintPlate plate)
    {
        Plate = plate;
        Filaments = (plate.Filaments ?? []).Select(f => new PlateFilamentViewModel(f)).ToList();
    }

    public PrintPlate Plate { get; }

    public int Number => Plate.Number;

    /// <summary>"Plate 2", or "Plate 2: Benchy" when the plate has a name.</summary>
    public string Title => string.IsNullOrWhiteSpace(Plate.Name) ? $"Plate {Plate.Number}" : $"Plate {Plate.Number}: {Plate.Name}";

    /// <summary>The estimated time and filament weight on one line, for example "41m • 15.3 g".</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>(2);
            if (SecondsToReadableTimeConverter.Instance.Convert(Plate.EstimatedTimeSeconds, typeof(string), null, CultureInfo.CurrentCulture)
                is string { Length: > 0 } time)
            {
                parts.Add(time);
            }

            if (PrintFileText.Weight(Plate.FilamentWeightGrams) is { } weight)
            {
                parts.Add(weight);
            }

            return string.Join(" • ", parts);
        }
    }

    public byte[]? ThumbnailData => Plate.Thumbnail?.Data;

    public IReadOnlyList<PlateFilamentViewModel> Filaments { get; }

    public string? Problem => Plate.Problem;

    public bool CanStart => Plate.Problem is null;
}

/// <summary>A filament of a plate and the spool it would print from.</summary>
public sealed class PlateFilamentViewModel(PlateFilament filament)
{
    /// <summary>The type, with the colour when known: "PLA #FFFFFF".</summary>
    public string Name => filament.Color is { Length: > 0 } color ? $"{filament.Type} #{color}" : filament.Type;

    /// <summary>The colour as a brush value ("#RRGGBB"), or null when unknown.</summary>
    public string? ColorHex => filament.Color is { Length: 6 } color ? $"#{color}" : null;

    public bool HasColor => ColorHex is not null;

    /// <summary>Where the printer feeds it from, or that nothing loaded matches.</summary>
    public string Source => filament.Source ?? "No matching spool";

    public bool IsMatched => filament.Source is not null;
}
