namespace OctaGrid.Extensions.Models;

/// <summary>A printable file on the printer and what its slicer recorded about the print.</summary>
/// <param name="FileName">The file's name on the printer; later requests identify the file by it.</param>
/// <param name="FileSize">The file's size in bytes.</param>
/// <param name="MaterialType">The filament types the file was sliced for, for example "PLA, PETG".</param>
/// <param name="NozzleDiameter">The nozzle diameter in millimetres the file was sliced for.</param>
/// <param name="EstimatedTimeSeconds">The slicer's estimate of the print time, in seconds.</param>
/// <param name="ModifiedAt">When the file was last written; some printers report only the date.</param>
/// <param name="FilamentWeightGrams">
/// The slicer's estimate of the filament used, in grams, over all materials.
/// </param>
public sealed record PrintMetadata(
    string FileName,
    long FileSize,
    string? MaterialType,
    double? NozzleDiameter,
    double? EstimatedTimeSeconds,
    DateTimeOffset? ModifiedAt = null,
    double? FilamentWeightGrams = null);
