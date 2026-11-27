using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Connectors.BambuLab;

/// <summary>A filament a plate prints with.</summary>
/// <param name="Id">The filament's number in the project, from 1. The start command maps filaments by it.</param>
/// <param name="Type">The filament type, such as "PLA".</param>
/// <param name="Color">The colour as six upper-case hex digits (RRGGBB), or empty when the file has none.</param>
internal sealed record BambuPlateFilament(int Id, string Type, string Color);

/// <summary>A sliced plate of a .gcode.3mf.</summary>
/// <param name="Index">The plate's number, from 1: its G-code is <c>Metadata/plate_&lt;Index&gt;.gcode</c>.</param>
/// <param name="Filaments">The filaments the plate uses.</param>
/// <param name="EstimatedTimeSeconds">The slicer's estimate of the print time.</param>
/// <param name="WeightGrams">The filament the plate uses, in grams.</param>
internal sealed record BambuPlate(
    int Index,
    IReadOnlyList<BambuPlateFilament> Filaments,
    double? EstimatedTimeSeconds = null,
    double? WeightGrams = null);

/// <summary>Reads the sliced plates from <c>Metadata/slice_info.config</c>, which lists only plates that have G-code.</summary>
internal static class BambuSliceInfo
{
    /// <summary>The entry of a .gcode.3mf that describes its sliced plates.</summary>
    public const string EntryName = "Metadata/slice_info.config";

    private const string PlatePicturePrefix = "Metadata/plate_";

    // Bambu Studio writes PNG; other slicers' 3MFs can carry JPEG, as the file list's thumbnails already allow.
    // In order of preference, for a plate that has more than one.
    private static readonly (string Extension, string ContentType)[] PlatePictureFormats =
    [
        (".png", "image/png"),
        (".jpg", "image/jpeg"),
        (".jpeg", "image/jpeg"),
    ];

    /// <summary>
    /// Whether an entry is a plate's picture (<c>Metadata/plate_&lt;n&gt;.png</c>, or .jpg/.jpeg), not one of the
    /// other pictures Bambu Studio writes per plate, such as <c>plate_1_small.png</c> or <c>plate_no_light_1.png</c>.
    /// </summary>
    public static bool IsPlatePicture(string entryName)
    {
        if (!entryName.StartsWith(PlatePicturePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var (extension, _) in PlatePictureFormats)
        {
            if (entryName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                var number = entryName.AsSpan(PlatePicturePrefix.Length, entryName.Length - PlatePicturePrefix.Length - extension.Length);
                return number.Length > 0 && !number.ContainsAnyExceptInRange('0', '9');
            }
        }

        return false;
    }

    /// <summary>The picture of <paramref name="plate"/> among entries read from its file, or null when it has none.</summary>
    /// <param name="entries">Entries keyed by name, case-insensitively, as the 3MF reader returns them.</param>
    public static PrintThumbnail? PlatePicture(BambuPlate plate, IReadOnlyDictionary<string, byte[]> entries)
    {
        foreach (var (extension, contentType) in PlatePictureFormats)
        {
            if (entries.TryGetValue($"{PlatePicturePrefix}{plate.Index}{extension}", out var data) && data.Length > 0)
            {
                return new PrintThumbnail(contentType, data);
            }
        }

        return null;
    }

    /// <summary>The sliced plates of the entry's content; none when it is not valid XML.</summary>
    public static IReadOnlyList<BambuPlate> ReadPlates(byte[] sliceInfo)
    {
        try
        {
            using var stream = new MemoryStream(sliceInfo, writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            return ReadPlates(XDocument.Load(reader));
        }
        catch (XmlException)
        {
            return [];
        }
    }

    /// <summary>The sliced plates in the order the file lists them. Plates without a valid index are skipped.</summary>
    public static IReadOnlyList<BambuPlate> ReadPlates(XDocument sliceInfo) =>
        sliceInfo.Descendants("plate")
            .Select(plate => (Index: ParseInt(Metadata(plate, "index")), Plate: plate))
            .Where(p => p.Index > 0)
            .Select(p => new BambuPlate(
                p.Index,
                p.Plate.Elements("filament")
                    .Select(f => (Id: ParseInt((string?)f.Attribute("id")), Filament: f))
                    .Where(f => f.Id > 0)
                    .Select(f => new BambuPlateFilament(
                        f.Id,
                        ((string?)f.Filament.Attribute("type"))?.Trim() ?? string.Empty,
                        BambuAmsTracker.NormalizeColor((string?)f.Filament.Attribute("color")) ?? string.Empty))
                    .ToList(),
                ParseDouble(Metadata(p.Plate, "prediction")),
                ParseDouble(Metadata(p.Plate, "weight"))))
            .ToList();

    private static string? Metadata(XElement plate, string key) =>
        plate.Elements("metadata").FirstOrDefault(m => (string?)m.Attribute("key") == key)?.Attribute("value")?.Value;

    private static int ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : 0;

    private static double? ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number > 0 ? number : null;
}
