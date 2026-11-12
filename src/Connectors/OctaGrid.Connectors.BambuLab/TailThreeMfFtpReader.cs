using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using FluentFTP;
using Microsoft.Extensions.Logging;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Connectors.BambuLab;

public static class TailThreeMfFtpReader
{
    private const int CdHeaderFixedSize = 46;
    private const uint CdHeaderSignature = 0x02014b50;
    private const int DefaultTailSize = 64 * 1024;
    private const int EocdMinSize = 22;
    private const uint EocdSignature = 0x06054b50;
    private const int LocalExtraMargin = 512;
    private const int LocalHeaderFixedSize = 30;
    private const uint LocalHeaderSignature = 0x04034b50;
    private const int MaxCentralDirectoryBytes = 8 * 1024 * 1024;
    private const int MaxEntryBytes = 50 * 1024 * 1024;
    private const int MaxTailSize = 1024 * 1024;

    // over-read to avoid a 2nd request when local extra field > CD extra field
    private const long MergeGapBytes = 64 * 1024;

    private const string SliceInfoPath = "Metadata/slice_info.config";
    private const long SmallFileThreshold = 1024 * 1024;
    private const int TailHintMargin = 8 * 1024;
    private const int TailHintStep = 16 * 1024;

    // merge ranged reads if entries are closer than this
    private const uint Zip64Marker = 0xFFFFFFFF;

    private static readonly string[] PreferredThumbnails =
    [
        "Metadata/plate_1.png",
        "Metadata/plate_1.jpg",
        "Metadata/thumbnail.png",
        "Metadata/thumbnail.jpg",
    ];

    private static int _tailHint = DefaultTailSize;

    public static Task<(PrintMetadata Metadata, byte[]? Thumbnail)?> ExtractFromTailAsync(
        AsyncFtpClient ftp,
        FtpListItem item,
        ILogger? logger = null,
        CancellationToken cancellationToken = default) =>
        ExtractFromTailAsync(ftp, item, extractThumbnail: true, logger, cancellationToken);

    public static async Task<(PrintMetadata Metadata, byte[]? Thumbnail)?> ExtractFromTailAsync(
        AsyncFtpClient ftp,
        FtpListItem item,
        bool extractThumbnail,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        if (item.Size <= 0)
        {
            return null;
        }

        // Small files: just download them whole.
        if (item.Size <= SmallFileThreshold)
        {
            try
            {
                return await DownloadSmallAsync(ftp, item, logger, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to download small 3MF {File}", item.FullName);
                return null;
            }
        }

        // Large files: ranged reads (EOCD -> central directory -> only the entries we need).
        try
        {
            var fast = await TryExtractViaRangesAsync(ftp, item, extractThumbnail, logger, cancellationToken);
            if (fast is not null)
            {
                return fast;
            }

            logger?.LogInformation("Tail extraction not applicable for {File}, using full download", item.FullName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Tail extraction failed for {File}, using full download", item.FullName);
        }

        try
        {
            return await FullDownloadFallbackAsync(ftp, item, logger, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Full download fallback failed for {File}", item.FullName);
            return null;
        }
    }

    // ---------------------------------------------------------------------
    // Fast path
    // ---------------------------------------------------------------------

    private static PrintMetadata BuildMetadata(FtpListItem item, string? material, double? nozzle, double? timeSeconds) =>
        new(
            FileName: item.Name,
            FileSize: ClampSize(item.Size),
            MaterialType: material,
            NozzleDiameter: nozzle,
            EstimatedTimeSeconds: timeSeconds);

    private static int ClampSize(long size) => (int)Math.Min(size, int.MaxValue);

    private static async Task<(PrintMetadata Metadata, byte[]? Thumbnail)?> DownloadSmallAsync(
        AsyncFtpClient ftp,
        FtpListItem item,
        ILogger? logger,
        CancellationToken ct)
    {
        using var ms = new MemoryStream((int)item.Size);
        if (!await ftp.DownloadStream(ms, item.FullName, token: ct))
        {
            logger?.LogWarning("FTP download returned failure for {File}", item.FullName);
            return null;
        }

        ms.Position = 0;
        return ThreeMfExtractor.ExtractFromStream(ms, item.Name, ClampSize(item.Size));
    }

    private static long EstimateEnd(ZipEntryRef e, long fileSize) =>
        Math.Min(fileSize, e.LocalHeaderOffset + LocalHeaderFixedSize + e.NameByteLength + LocalExtraMargin + e.CompressedSize);

    private static async Task<byte[]?> ExtractEntryAsync(
        AsyncFtpClient ftp,
        string path,
        byte[] buffer,
        long bufferStart,
        ZipEntryRef e,
        CancellationToken ct)
    {
        var rel = checked((int)(e.LocalHeaderOffset - bufferStart));
        if (rel < 0 || rel + LocalHeaderFixedSize > buffer.Length)
        {
            throw new InvalidDataException($"Local header for '{e.Name}' is outside the downloaded range.");
        }

        var header = buffer.AsSpan(rel, LocalHeaderFixedSize);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != LocalHeaderSignature)
        {
            throw new InvalidDataException($"Invalid local header signature for '{e.Name}'.");
        }

        int nameLen = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(26, 2));
        int extraLen = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(28, 2));
        var dataStart = e.LocalHeaderOffset + LocalHeaderFixedSize + nameLen + extraLen;
        var compSize = (int)e.CompressedSize;

        if (compSize == 0)
        {
            return [];
        }

        var relData = dataStart - bufferStart;
        Stream payload;
        if (relData >= 0 && relData + compSize <= buffer.Length)
        {
            payload = new MemoryStream(buffer, (int)relData, compSize, writable: false);
        }
        else
        {
            // Local extra field was larger than our margin: rare, fetch exactly the payload.
            var exact = await ReadRangeAsync(ftp, path, dataStart, compSize, ct);
            payload = new MemoryStream(exact, writable: false);
        }

        await using (payload)
        {
            if (e.Method == 0)
            {
                var stored = new byte[compSize];
                await payload.ReadExactlyAsync(stored, ct);
                return stored;
            }

            // Method 8: Deflate
            var output = new byte[(int)e.UncompressedSize];
            await using var deflate = new DeflateStream(payload, CompressionMode.Decompress);
            await deflate.ReadExactlyAsync(output, ct);
            return output;
        }
    }

    private static ZipEntryRef? FindThumbnail(Dictionary<string, ZipEntryRef> entries)
    {
        foreach (var preferred in PreferredThumbnails)
        {
            if (entries.TryGetValue(preferred, out var e))
            {
                return e;
            }
        }

        // Deterministic heuristic fallback
        return entries.Values
            .Where(e =>
                (e.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                 e.Name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)) &&
                !e.Name.Contains("_small", StringComparison.OrdinalIgnoreCase) &&
                (e.Name.Contains("plate_1", StringComparison.OrdinalIgnoreCase) ||
                 e.Name.Contains("thumbnail", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(e => (ZipEntryRef?)e)
            .FirstOrDefault();
    }

    // ---------------------------------------------------------------------
    // Whole-file paths
    // ---------------------------------------------------------------------
    private static async Task<(PrintMetadata Metadata, byte[]? Thumbnail)?> FullDownloadFallbackAsync(
        AsyncFtpClient ftp,
        FtpListItem item,
        ILogger? logger,
        CancellationToken ct)
    {
        // Spool to a temp file instead of holding a multi-hundred-MB file in memory.
        var tempPath = Path.Combine(Path.GetTempPath(), $"octagrid-{Guid.NewGuid():N}.3mf");

        await using var fs = new FileStream(
            tempPath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose);

        if (!await ftp.DownloadStream(fs, item.FullName, token: ct))
        {
            logger?.LogWarning("FTP full download returned failure for {File}", item.FullName);
            return null;
        }

        fs.Position = 0;
        return ThreeMfExtractor.ExtractFromStream(fs, item.Name, ClampSize(item.Size));
    }

    private static bool IsReadable(ZipEntryRef e, out string? reason)
    {
        reason = null;
        if (e.NeedsZip64) { reason = "ZIP64 entry"; return false; }
        if ((e.Flags & 0x0001) != 0) { reason = "encrypted"; return false; }
        if (e.Method != 0 && e.Method != 8) { reason = $"unsupported method {e.Method}"; return false; }
        if (e.CompressedSize > MaxEntryBytes || e.UncompressedSize > MaxEntryBytes) { reason = "too large"; return false; }
        return true;
    }

    private static Dictionary<string, ZipEntryRef> ParseCentralDirectory(byte[] cd)
    {
        var result = new Dictionary<string, ZipEntryRef>(StringComparer.OrdinalIgnoreCase);
        var i = 0;

        while (i + CdHeaderFixedSize <= cd.Length)
        {
            var h = cd.AsSpan(i);
            if (BinaryPrimitives.ReadUInt32LittleEndian(h) != CdHeaderSignature)
            {
                break;
            }

            var flags = BinaryPrimitives.ReadUInt16LittleEndian(h.Slice(8, 2));
            var method = BinaryPrimitives.ReadUInt16LittleEndian(h.Slice(10, 2));
            var compSize = BinaryPrimitives.ReadUInt32LittleEndian(h.Slice(20, 4));
            var uncompSize = BinaryPrimitives.ReadUInt32LittleEndian(h.Slice(24, 4));
            var nameLen = BinaryPrimitives.ReadUInt16LittleEndian(h.Slice(28, 2));
            var extraLen = BinaryPrimitives.ReadUInt16LittleEndian(h.Slice(30, 2));
            var commentLen = BinaryPrimitives.ReadUInt16LittleEndian(h.Slice(32, 2));
            var localOffset = BinaryPrimitives.ReadUInt32LittleEndian(h.Slice(42, 4));

            var total = CdHeaderFixedSize + nameLen + extraLen + commentLen;
            if (i + total > cd.Length)
            {
                break;
            }

            // Bit 11 = UTF-8 names; otherwise the spec says CP437 (Latin1 is a safe approximation for ASCII names).
            var encoding = (flags & 0x0800) != 0 ? Encoding.UTF8 : Encoding.Latin1;
            var name = encoding.GetString(cd, i + CdHeaderFixedSize, nameLen)
                .Replace('\\', '/')
                .TrimStart('/');

            var needsZip64 = compSize == Zip64Marker || uncompSize == Zip64Marker || localOffset == Zip64Marker;

            result[name] = new ZipEntryRef(name, flags, method, compSize, uncompSize, localOffset, nameLen, needsZip64);
            i += total;
        }

        return result;
    }

    // ---------------------------------------------------------------------
    // Metadata
    // ---------------------------------------------------------------------
    private static (string? Mat, double? Nozzle, double? Time) ParseSliceInfoDoc(Stream stream)
    {
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
            };

            using var reader = XmlReader.Create(stream, settings);
            var doc = XDocument.Load(reader);

            double totalPrediction = 0;
            var hasPrediction = false;

            foreach (var meta in doc.Descendants("metadata"))
            {
                if ((string?)meta.Attribute("key") == "prediction" &&
                    double.TryParse((string?)meta.Attribute("value"),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
                {
                    totalPrediction += val;
                    hasPrediction = true;
                }
            }

            var filamentTypes = doc.Descendants("filament")
                .Select(f => (string?)f.Attribute("type"))
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var nozzleStr = doc.Descendants("filament")
                    .Select(f => (string?)f.Attribute("nozzle_diameter"))
                    .FirstOrDefault(d => !string.IsNullOrWhiteSpace(d))
                ?? doc.Descendants("metadata")
                    .FirstOrDefault(m => (string?)m.Attribute("key") == "nozzle_diameters")
                    ?.Attribute("value")?.Value.Split(',', ';').FirstOrDefault();

            double? nozzle = double.TryParse(nozzleStr?.Trim(),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;

            var material = filamentTypes.Count > 0 ? string.Join(", ", filamentTypes) : null;

            return (material, nozzle, hasPrediction ? totalPrediction : null);
        }
        catch (XmlException)
        {
            return (null, null, null);
        }
    }

    private static async Task<Dictionary<string, byte[]>> ReadEntriesAsync(
    AsyncFtpClient ftp,
    string path,
    long fileSize,
    List<ZipEntryRef> wanted,
    byte[] tail,
    long tailOffset,
    ILogger? logger,
    CancellationToken ct)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var remote = new List<ZipEntryRef>(wanted.Count);

        foreach (var e in wanted)
        {
            if (!IsReadable(e, out var reason))
            {
                logger?.LogDebug("Skipping entry {Entry}: {Reason}", e.Name, reason);
                continue;
            }

            if (e.LocalHeaderOffset >= tailOffset)
            {
                // Already downloaded as part of the tail: zero extra requests.
                var data = await ExtractEntryAsync(ftp, path, tail, tailOffset, e, ct);
                if (data is not null)
                {
                    result[e.Name] = data;
                }
            }
            else
            {
                remote.Add(e);
            }
        }

        remote.Sort((a, b) => a.LocalHeaderOffset.CompareTo(b.LocalHeaderOffset));

        var i = 0;
        while (i < remote.Count)
        {
            var start = remote[i].LocalHeaderOffset;
            var end = EstimateEnd(remote[i], fileSize);

            var j = i + 1;
            while (j < remote.Count && remote[j].LocalHeaderOffset <= end + MergeGapBytes)
            {
                end = Math.Max(end, EstimateEnd(remote[j], fileSize));
                j++;
            }

            var buffer = await ReadRangeAsync(ftp, path, start, checked((int)(end - start)), ct);

            for (var k = i; k < j; k++)
            {
                var data = await ExtractEntryAsync(ftp, path, buffer, start, remote[k], ct);
                if (data is not null)
                {
                    result[remote[k].Name] = data;
                }
            }

            i = j;
        }

        return result;
    }

    private static async Task<byte[]> ReadRangeAsync(
        AsyncFtpClient ftp,
        string path,
        long offset,
        int length,
        CancellationToken ct)
    {
        var buffer = new byte[length];
        if (length == 0)
        {
            return buffer;
        }

        // fileLen lets FluentFTP know where we intend to stop so it can finish the transfer cleanly.
        using (var stream = await ftp.OpenRead(
            path, FtpDataType.Binary, restart: offset, fileLen: offset + length, token: ct))
        {
            await stream.ReadExactlyAsync(buffer.AsMemory(0, length), ct);
        }

        try
        {
            await ftp.GetReply(ct);
        }
        catch
        {
            // If the server already sent completion or connection closed, ignore
        }

        return buffer;
    }

    private static async Task<(PrintMetadata Metadata, byte[]? Thumbnail)?> TryExtractViaRangesAsync(
                                                        AsyncFtpClient ftp,
        FtpListItem item,
        bool extractThumbnail,
        ILogger? logger,
        CancellationToken ct)
    {
        var fileSize = item.Size;
        var path = item.FullName;

        // 1. Tail
        var tailSize = Volatile.Read(ref _tailHint);
        var tailOffset = Math.Max(0, fileSize - tailSize);
        var tail = await ReadRangeAsync(ftp, path, tailOffset, (int)(fileSize - tailOffset), ct);

        // 2. EOCD (validated); null => ZIP64 / multi-disk / not found
        if (!TryFindEocd(tail, out var cdOffset, out var cdSize))
        {
            return null;
        }

        if (cdSize <= 0 || cdSize > MaxCentralDirectoryBytes || cdOffset < 0 || cdOffset + cdSize > fileSize)
        {
            return null;
        }

        // 3. Central directory: reuse whatever is already in the tail, fetch only the missing head part
        var cd = new byte[cdSize];
        if (cdOffset >= tailOffset)
        {
            var startInTail = (int)(cdOffset - tailOffset);
            if (startInTail + cdSize > tail.Length)
            {
                return null;
            }
            tail.AsSpan(startInTail, (int)cdSize).CopyTo(cd);
        }
        else
        {
            var headLen = (int)Math.Min(tailOffset - cdOffset, cdSize);
            var head = await ReadRangeAsync(ftp, path, cdOffset, headLen, ct);
            head.CopyTo(cd, 0);

            var fromTail = (int)cdSize - headLen;
            if (fromTail > 0)
            {
                tail.AsSpan(0, fromTail).CopyTo(cd.AsSpan(headLen));
            }
        }

        var entries = ParseCentralDirectory(cd);
        if (entries.Count == 0)
        {
            return null;
        }

        // 4. Choose entries to read
        var wanted = new List<ZipEntryRef>(2);
        var hasSlice = entries.TryGetValue(SliceInfoPath, out var sliceEntry);
        if (hasSlice)
        {
            wanted.Add(sliceEntry);
        }

        var thumbEntry = extractThumbnail ? FindThumbnail(entries) : null;
        if (thumbEntry is { } te)
        {
            wanted.Add(te);
        }

        // 5. Read them with as few requests as possible
        var payloads = await ReadEntriesAsync(ftp, path, fileSize, wanted, tail, tailOffset, logger, ct);
        UpdateTailHint(fileSize, wanted, logger);

        string? material = null;
        double? nozzle = null;
        double? time = null;

        if (hasSlice && payloads.TryGetValue(sliceEntry.Name, out var sliceData))
        {
            using var ms = new MemoryStream(sliceData, writable: false);
            (material, nozzle, time) = ParseSliceInfoDoc(ms);
        }

        byte[]? thumbnail = null;
        if (thumbEntry is { } t)
        {
            payloads.TryGetValue(t.Name, out thumbnail);
        }

        return (BuildMetadata(item, material, nozzle, time), thumbnail);
    }

    private static bool TryFindEocd(byte[] tail, out long cdOffset, out long cdSize)
    {
        cdOffset = 0;
        cdSize = 0;

        for (var i = tail.Length - EocdMinSize; i >= 0; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i, 4)) != EocdSignature)
            {
                continue;
            }

            // The real EOCD's comment runs exactly to the end of the file.
            var commentLen = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + 20, 2));
            if (i + EocdMinSize + commentLen != tail.Length)
            {
                continue;
            }

            var diskNo = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + 4, 2));
            var cdDisk = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + 6, 2));
            if (diskNo != 0 || cdDisk != 0)
            {
                return false; // multi-disk archives are not supported
            }

            var size = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i + 12, 4));
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i + 16, 4));
            if (size == Zip64Marker || offset == Zip64Marker)
            {
                return false; // ZIP64 => caller falls back to full download
            }

            cdSize = size;
            cdOffset = offset;
            return true;
        }

        return false;
    }

    private static void UpdateTailHint(long fileSize, List<ZipEntryRef> wanted, ILogger? logger)
    {
        if (wanted.Count == 0)
        {
            return;
        }

        var earliest = wanted.Min(e => e.LocalHeaderOffset);
        var span = fileSize - earliest + TailHintMargin;

        var hint = span <= MaxTailSize
            ? (int)Math.Max(DefaultTailSize, (span + TailHintStep - 1) / TailHintStep * TailHintStep)
            : DefaultTailSize; // metadata too far from the end to be worth a big tail

        if (Interlocked.Exchange(ref _tailHint, hint) != hint)
        {
            logger?.LogDebug("3MF tail hint is now {Hint} bytes (span {Span})", hint, span);
        }
    }

    // ---------------------------------------------------------------------
    // FTP ranged read
    // ---------------------------------------------------------------------
    // ---------------------------------------------------------------------
    // ZIP parsing
    // ---------------------------------------------------------------------
    private readonly record struct ZipEntryRef(
        string Name,
        ushort Flags,
        ushort Method,
        long CompressedSize,
        long UncompressedSize,
        long LocalHeaderOffset,
        int NameByteLength,
        bool NeedsZip64);
}
