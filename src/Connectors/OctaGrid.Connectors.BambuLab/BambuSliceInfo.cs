using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace OctaGrid.Connectors.BambuLab;

/// <summary>A filament a plate prints with.</summary>
/// <param name="Id">The filament's number in the project, from 1. The start command maps filaments by it.</param>
/// <param name="Type">The filament type, such as "PLA".</param>
/// <param name="Color">The colour as six upper-case hex digits (RRGGBB), or empty when the file has none.</param>
internal sealed record BambuPlateFilament(int Id, string Type, string Color);

/// <summary>A sliced plate of a .gcode.3mf.</summary>
/// <param name="Index">The plate's number, from 1: its G-code is <c>Metadata/plate_&lt;Index&gt;.gcode</c>.</param>
/// <param name="Filaments">The filaments the plate uses.</param>
internal sealed record BambuPlate(int Index, IReadOnlyList<BambuPlateFilament> Filaments);

/// <summary>Reads the sliced plates from <c>Metadata/slice_info.config</c>, which lists only plates that have G-code.</summary>
internal static class BambuSliceInfo
{
    /// <summary>The entry of a .gcode.3mf that describes its sliced plates.</summary>
    public const string EntryName = "Metadata/slice_info.config";

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
            .Select(plate => (Index: PlateIndex(plate), Plate: plate))
            .Where(p => p.Index > 0)
            .Select(p => new BambuPlate(p.Index, p.Plate.Elements("filament")
                .Select(f => (Id: ParseInt((string?)f.Attribute("id")), Filament: f))
                .Where(f => f.Id > 0)
                .Select(f => new BambuPlateFilament(
                    f.Id,
                    ((string?)f.Filament.Attribute("type"))?.Trim() ?? string.Empty,
                    BambuAmsTracker.NormalizeColor((string?)f.Filament.Attribute("color")) ?? string.Empty))
                .ToList()))
            .ToList();

    private static int PlateIndex(XElement plate) =>
        ParseInt(plate.Elements("metadata").FirstOrDefault(m => (string?)m.Attribute("key") == "index")?.Attribute("value")?.Value);

    private static int ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : 0;
}
