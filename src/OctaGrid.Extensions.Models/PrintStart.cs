namespace OctaGrid.Extensions.Models;

/// <summary>
/// What the user can choose when starting a file: the plate to print and the connector's start options. The app shows
/// it before the print starts and sends the choices back with the start.
/// </summary>
/// <param name="FileName">The file, as the printer's listing names it.</param>
/// <param name="Plates">The plates the file can print, at least one. The app asks for a choice only when there are several.</param>
/// <param name="Options">
/// Start options the user can change, such as bed levelling. The app shows them as a form with their default values and
/// sends every value back by <see cref="SettingField.Key"/>.
/// </param>
public sealed record PrintStartPlan(
    string FileName,
    IReadOnlyList<PrintPlate> Plates,
    IReadOnlyList<SettingField> Options);

/// <summary>A plate of a file: one print job the file can start.</summary>
/// <param name="Number">The plate's number, from 1, as <see cref="PrintStartChoices.Plate"/> names it.</param>
/// <param name="Name">The plate's name, when the file has one.</param>
/// <param name="EstimatedTimeSeconds">How long the slicer expects the plate to print.</param>
/// <param name="FilamentWeightGrams">How much filament the slicer expects the plate to use.</param>
/// <param name="Filaments">The filaments the plate uses and where each one would come from; null when not known.</param>
/// <param name="Thumbnail">A picture of the plate.</param>
/// <param name="Problem">Why the plate cannot start now, written for the user; null when it can.</param>
public sealed record PrintPlate(
    int Number,
    string? Name = null,
    double? EstimatedTimeSeconds = null,
    double? FilamentWeightGrams = null,
    IReadOnlyList<PlateFilament>? Filaments = null,
    PrintThumbnail? Thumbnail = null,
    string? Problem = null);

/// <summary>A filament a plate prints with.</summary>
/// <param name="Type">The filament type, such as "PLA".</param>
/// <param name="Color">The colour as six hex digits (RRGGBB), when known.</param>
/// <param name="Source">Where the printer would feed it from, such as "AMS A2" or "External spool"; null when nothing matches.</param>
public sealed record PlateFilament(string Type, string? Color = null, string? Source = null);

/// <summary>What the user chose when starting a file.</summary>
/// <param name="Plate">The plate to print, by <see cref="PrintPlate.Number"/>; null for the file's only plate.</param>
/// <param name="Options">The start options by <see cref="SettingField.Key"/>; options left out keep their defaults.</param>
public sealed record PrintStartChoices(int? Plate = null, IReadOnlyDictionary<string, string>? Options = null);
