using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Agent.Core.Clients.BambuMQTT;

public static class ThreeMfExtractor
{
    private const long MaxThumbnailBytes = 10 * 1024 * 1024; // 10 MB safety limit
    private const string ThumbnailRelType = "http://schemas.openxmlformats.org/package/2006/relationships/metadata/thumbnail";
    private static readonly XNamespace RelsNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>
    /// Opens a stream with the specified file path and extracts metadata and thumbnail.
    /// </summary>
    public static (PrintMetadata Metadata, byte[]? Thumbnail) ExtractFromFile(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        using var stream = fileInfo.OpenRead();
        var safeSize = (int)Math.Min(fileInfo.Length, int.MaxValue);
        return ExtractFromStream(stream, fileInfo.Name, safeSize);
    }

    /// <summary>
    /// Reads a 3MF/gcode.3mf stream and extracts print metadata and the thumbnail bytes.
    /// </summary>
    public static (PrintMetadata Metadata, byte[]? Thumbnail) ExtractFromStream(Stream stream, string fileName, int? fileSize = null, bool leaveOpen = false)
    {
        var resolvedFileSize = fileSize ?? (stream.CanSeek ? (int)Math.Min(stream.Length, int.MaxValue) : 0);

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen);

        var (materialType, nozzleDiameter, estimatedTimeSeconds) = ExtractSliceInfo(archive);

        if (nozzleDiameter is null || materialType is null)
        {
            var (fallbackMat, fallbackNozzle) = ExtractJsonFallback(archive);
            materialType ??= fallbackMat;
            nozzleDiameter ??= fallbackNozzle;
        }

        var thumbnail = ExtractThumbnail(archive);

        var metadata = new PrintMetadata(
            FileName: fileName,
            FileSize: resolvedFileSize,
            MaterialType: materialType,
            NozzleDiameter: nozzleDiameter,
            EstimatedTimeSeconds: estimatedTimeSeconds
        );

        return (metadata, thumbnail);
    }

    private static double? ExtractDoubleFromJsonElement(JsonElement elem) => elem.ValueKind switch
    {
        JsonValueKind.Number when elem.TryGetDouble(out var d) => d,
        JsonValueKind.String when TryParseDouble(elem.GetString(), out var d) => d,
        JsonValueKind.Array when elem.GetArrayLength() > 0 => ExtractDoubleFromJsonElement(elem[0]),
        _ => null
    };

    private static (string? Material, double? Nozzle) ExtractJsonFallback(ZipArchive archive)
    {
        // Check Metadata/plate_*.json first, then Metadata/project_settings.config (also JSON in Bambu/Orca)
        var jsonEntry = archive.Entries.FirstOrDefault(e =>
                e.FullName.StartsWith("Metadata/plate_", StringComparison.OrdinalIgnoreCase) &&
                e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            ?? FindEntry(archive, "Metadata/project_settings.config");

        if (jsonEntry is null)
        {
            return (null, null);
        }

        try
        {
            using var stream = jsonEntry.Open();
            using var jsonDoc = JsonDocument.Parse(stream);
            var root = jsonDoc.RootElement;

            double? nozzle = null;
            if (root.TryGetProperty("nozzle_diameter", out var nozzleElem))
            {
                nozzle = ExtractDoubleFromJsonElement(nozzleElem);
            }

            string? material = null;
            if (root.TryGetProperty("filament_type", out var matElem))
            {
                material = ExtractStringFromJsonElement(matElem);
            }

            return (material, nozzle);
        }
        catch
        {
            return (null, null);
        }
    }

    private static (string? Material, double? Nozzle, double? TimeSeconds) ExtractSliceInfo(ZipArchive archive)
    {
        var entry = FindEntry(archive, "Metadata/slice_info.config");
        if (entry is null)
        {
            return (null, null, null);
        }

        try
        {
            using var stream = entry.Open();
            var doc = XDocument.Load(stream);

            // Sum predictions across plates (or single plate if only one is sliced)
            double totalPrediction = 0;
            var hasPrediction = false;

            foreach (var meta in doc.Descendants("metadata"))
            {
                if ((string?)meta.Attribute("key") == "prediction" &&
                    TryParseDouble((string?)meta.Attribute("value"), out var pred))
                {
                    totalPrediction += pred;
                    hasPrediction = true;
                }
            }

            double? estimatedTimeSeconds = hasPrediction ? totalPrediction : null;

            // Material types: <filament id="1" type="PETG" ... />
            var filamentTypes = doc.Descendants("filament")
                .Select(f => (string?)f.Attribute("type"))
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var materialType = filamentTypes.Count > 0 ? string.Join(", ", filamentTypes) : null;

            // Nozzle diameter from filament, nozzle element, or metadata key="nozzle_diameters"
            var nozzleDiamStr = doc.Descendants("filament")
                    .Select(f => (string?)f.Attribute("nozzle_diameter"))
                    .FirstOrDefault(d => !string.IsNullOrWhiteSpace(d))
                ?? doc.Descendants("nozzle")
                    .Select(n => (string?)n.Attribute("nozzle_diameter"))
                    .FirstOrDefault(d => !string.IsNullOrWhiteSpace(d))
                ?? doc.Descendants("metadata")
                    .FirstOrDefault(m => (string?)m.Attribute("key") == "nozzle_diameters")
                    ?.Attribute("value")?.Value?.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault();

            double? nozzleDiameter = TryParseDouble(nozzleDiamStr, out var nd) ? nd : null;

            return (materialType, nozzleDiameter, estimatedTimeSeconds);
        }
        catch
        {
            return (null, null, null);
        }
    }

    private static string? ExtractStringFromJsonElement(JsonElement elem) => elem.ValueKind switch
    {
        JsonValueKind.String => elem.GetString(),
        JsonValueKind.Array => string.Join(", ", elem.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)),
        _ => null
    };

    private static byte[]? ExtractThumbnail(ZipArchive archive)
    {
        var thumbnailPath = TryGetThumbnailPathFromRels(archive)
                             ?? TryGetThumbnailPathFromModelSettings(archive);

        var thumbEntry = !string.IsNullOrEmpty(thumbnailPath)
            ? FindEntry(archive, thumbnailPath)
            : null;

        thumbEntry ??= FindEntry(archive, "Metadata/plate_1.png")
                    ?? FindEntry(archive, "Metadata/thumbnail.png")
                    ?? archive.Entries.FirstOrDefault(e =>
                        (e.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                         e.FullName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)) &&
                        !e.FullName.Contains("_small", StringComparison.OrdinalIgnoreCase) && // Avoid tiny icons
                        (e.FullName.Contains("plate_1", StringComparison.OrdinalIgnoreCase) ||
                         e.FullName.Contains("thumbnail", StringComparison.OrdinalIgnoreCase) ||
                         e.FullName.Contains("cover", StringComparison.OrdinalIgnoreCase)));

        if (thumbEntry is null || thumbEntry.Length is <= 0 or > MaxThumbnailBytes)
        {
            return null;
        }

        try
        {
            using var thumbStream = thumbEntry.Open();
            var buffer = new byte[thumbEntry.Length];
            thumbStream.ReadExactly(buffer);
            return buffer;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Fast path with GetEntry, falling back to case-insensitive and slash-normalized lookup.
    /// </summary>
    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        return archive.GetEntry(normalized)
            ?? archive.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName.Replace('\\', '/').TrimStart('/'), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string? TryGetThumbnailPathFromModelSettings(ZipArchive archive)
    {
        var entry = FindEntry(archive, "Metadata/model_settings.config");
        if (entry is null)
        {
            return null;
        }

        try
        {
            using var stream = entry.Open();
            var doc = XDocument.Load(stream);
            return doc.Descendants("metadata")
                .FirstOrDefault(m => (string?)m.Attribute("key") == "thumbnail_file")
                ?.Attribute("value")?.Value?.TrimStart('/');
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetThumbnailPathFromRels(ZipArchive archive)
    {
        var relsEntry = FindEntry(archive, "_rels/.rels");
        if (relsEntry is null)
        {
            return null;
        }

        try
        {
            using var stream = relsEntry.Open();
            var doc = XDocument.Load(stream);
            return doc.Descendants(RelsNs + "Relationship")
                .FirstOrDefault(r => (string?)r.Attribute("Type") == ThumbnailRelType)
                ?.Attribute("Target")?.Value?.TrimStart('/');
        }
        catch
        {
            return null;
        }
    }

    private static bool TryParseDouble(string? value, out double result) =>
        double.TryParse(value?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
}
