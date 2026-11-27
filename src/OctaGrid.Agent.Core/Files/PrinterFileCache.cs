using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Agent.Core.Files;

/// <summary>
/// What the agent has read from printers' files (metadata and thumbnails), kept on disk so a file is read over the
/// network once, also across restarts, and whatever connector the printer uses. An entry belongs to a file as long
/// as the file's name, size and modified time are the same as when it was read.
/// <para>
/// Entries are kept after a file leaves the listing, so a card taken out and put back is not read again. They are
/// removed when no listing has shown them for <c>maxAge</c>, when the total size passes <c>maxBytes</c> (the
/// longest unseen first), and with their printer's folder when the printer is no longer one of the agent's.
/// </para>
/// The cache is best effort: a disk error is logged, and the file is read from the printer again.
/// </summary>
public sealed class PrinterFileCache
{
    /// <summary>How long an entry is kept after the last listing that showed its file.</summary>
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromDays(30);

    /// <summary>The most the cache keeps on disk, metadata and thumbnails together.</summary>
    public const long DefaultMaxBytes = 256L * 1024 * 1024;

    private const string IndexFileName = "index.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Lock _lock = new();
    private readonly ILogger _logger;
    private readonly long _maxBytes;
    private readonly TimeSpan _maxAge;
    private readonly Dictionary<Guid, PrinterIndex> _printers = [];
    private readonly string _root;
    private readonly TimeProvider _time;
    private bool _loaded;

    /// <param name="root">The folder the cache keeps its files in, one subfolder per printer.</param>
    public PrinterFileCache(
        string root,
        TimeProvider? time = null,
        ILogger<PrinterFileCache>? logger = null,
        TimeSpan? maxAge = null,
        long maxBytes = DefaultMaxBytes)
    {
        _root = root;
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger<PrinterFileCache>.Instance;
        _maxAge = maxAge ?? DefaultMaxAge;
        _maxBytes = maxBytes;
    }

    /// <summary>The default folder: <c>%LocalAppData%/OctaGrid.Data/file-cache</c>. A cache, so it does not roam.</summary>
    public static string DefaultRoot { get; } = Path.Combine(AgentLocalData.Root, "file-cache");

    /// <summary>
    /// Takes a printer's file listing: remembers which files it shows, fills in the details of files read before
    /// and keeps the details the listing itself carries. Returns the listing with what the cache knows.
    /// </summary>
    public IReadOnlyList<PrintMetadata> ApplyListing(Guid printerId, IReadOnlyList<PrintMetadata> listing)
    {
        lock (_lock)
        {
            var printer = Printer(printerId);
            var now = _time.GetUtcNow();
            printer.Listed = listing.ToDictionary(f => f.FileName, f => (f.FileSize, f.ModifiedAt), StringComparer.OrdinalIgnoreCase);

            var result = new List<PrintMetadata>(listing.Count);
            foreach (var file in listing)
            {
                var key = Key(file.FileName, file.FileSize, file.ModifiedAt);
                if (printer.Entries.TryGetValue(key, out var entry))
                {
                    entry.LastSeen = now;
                    result.Add(file.DetailsPending && entry.Metadata is { } cached ? cached : file);
                    if (!file.DetailsPending && entry.Metadata is null)
                    {
                        entry.Metadata = file;
                    }
                }
                else
                {
                    printer.Entries[key] = new Entry
                    {
                        Name = file.FileName,
                        Size = file.FileSize,
                        ModifiedAt = file.ModifiedAt,
                        LastSeen = now,
                        Metadata = file.DetailsPending ? null : file,
                    };
                    result.Add(file);
                }
            }

            printer.Dirty = true;
            RemoveExpired(printer, now);
            Save(printerId, printer);
            return result;
        }
    }

    /// <summary>Keeps the details of files of the last listing; details of other files are not kept.</summary>
    public void StoreDetails(Guid printerId, IReadOnlyList<PrintMetadata> details)
    {
        lock (_lock)
        {
            var printer = Printer(printerId);
            foreach (var file in details.Where(f => !f.DetailsPending))
            {
                if (EntryOfListed(printer, file.FileName) is { } entry)
                {
                    entry.Metadata = file;
                    printer.Dirty = true;
                }
            }

            Save(printerId, printer);
        }
    }

    /// <summary>
    /// The thumbnail of a file of the last listing, if it was kept: true with the picture, or true with null when
    /// the file is known to have none. False when the cache does not know, so the printer must be asked.
    /// </summary>
    public bool TryGetThumbnail(Guid printerId, string fileName, out PrintThumbnail? thumbnail)
    {
        thumbnail = null;
        lock (_lock)
        {
            var printer = Printer(printerId);
            if (EntryOfListed(printer, fileName) is not { ThumbnailKnown: true } entry)
            {
                return false;
            }

            if (entry.ThumbnailFile is null)
            {
                return true;
            }

            try
            {
                thumbnail = new PrintThumbnail(entry.ThumbnailContentType ?? "image/png", File.ReadAllBytes(Path.Combine(Folder(printerId), entry.ThumbnailFile)));
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "Cached thumbnail of '{FileName}' on printer {PrinterId} could not be read", fileName, printerId);
                entry.ThumbnailKnown = false;
                entry.ThumbnailFile = null;
                printer.Dirty = true;
                return false;
            }
        }
    }

    /// <summary>Keeps the thumbnail of a file of the last listing, or that it has none.</summary>
    public void StoreThumbnail(Guid printerId, string fileName, PrintThumbnail? thumbnail)
    {
        lock (_lock)
        {
            var printer = Printer(printerId);
            if (EntryOfListed(printer, fileName) is not { } entry)
            {
                return;
            }

            try
            {
                DeleteThumbnail(printerId, entry);
                if (thumbnail is { Data.Length: > 0 })
                {
                    Directory.CreateDirectory(Folder(printerId));
                    var file = $"{Hash(entry.Key)}.img";
                    File.WriteAllBytes(Path.Combine(Folder(printerId), file), thumbnail.Data);
                    entry.ThumbnailFile = file;
                    entry.ThumbnailContentType = thumbnail.ContentType;
                    entry.ThumbnailBytes = thumbnail.Data.Length;
                }

                entry.ThumbnailKnown = true;
                printer.Dirty = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "Thumbnail of '{FileName}' on printer {PrinterId} could not be kept", fileName, printerId);
            }

            Save(printerId, printer);
            EnforceSizeLimit();
        }
    }

    /// <summary>
    /// Removes what is kept for printers that are no longer the agent's, and entries no listing has shown for too
    /// long. Run when the agent gets its printers, which is also at startup.
    /// </summary>
    public void Prune(IReadOnlyCollection<Guid> printers)
    {
        lock (_lock)
        {
            LoadAll();
            var keep = printers.ToHashSet();
            foreach (var printerId in _printers.Keys.Where(id => !keep.Contains(id)).ToList())
            {
                _logger.LogInformation("Removing cached files of printer {PrinterId}: it is no longer one of the agent's printers", printerId);
                _printers.Remove(printerId);
                DeleteFolder(printerId);
            }

            var now = _time.GetUtcNow();
            foreach (var (printerId, printer) in _printers.ToList())
            {
                RemoveExpired(printer, now);
                Save(printerId, printer);
            }

            EnforceSizeLimit();
        }
    }

    // A printer that has not answered a listing for long loses its entries one by one, and its folder with the last.
    private void RemoveExpired(PrinterIndex printer, DateTimeOffset now)
    {
        foreach (var (key, entry) in printer.Entries.Where(e => now - e.Value.LastSeen > _maxAge).ToList())
        {
            RemoveEntry(printer, key, entry);
        }
    }

    private void EnforceSizeLimit()
    {
        var total = _printers.Values.Sum(p => p.Entries.Values.Sum(e => e.Bytes));
        if (total <= _maxBytes)
        {
            return;
        }

        var oldestFirst = _printers
            .SelectMany(p => p.Value.Entries.Select(e => (Printer: p.Value, e.Key, Entry: e.Value)))
            .OrderBy(e => e.Entry.LastSeen)
            .ToList();
        foreach (var (printer, key, entry) in oldestFirst)
        {
            if (total <= _maxBytes)
            {
                break;
            }

            total -= entry.Bytes;
            RemoveEntry(printer, key, entry);
        }

        foreach (var (printerId, printer) in _printers.ToList())
        {
            Save(printerId, printer);
        }
    }

    private void RemoveEntry(PrinterIndex printer, string key, Entry entry)
    {
        DeleteThumbnail(printer.Id, entry);
        printer.Entries.Remove(key);
        printer.Dirty = true;
    }

    private static Entry? EntryOfListed(PrinterIndex printer, string fileName) =>
        printer.Listed.TryGetValue(fileName, out var listed) &&
        printer.Entries.TryGetValue(Key(fileName, listed.Size, listed.ModifiedAt), out var entry)
            ? entry
            : null;

    private PrinterIndex Printer(Guid printerId)
    {
        if (!_printers.TryGetValue(printerId, out var printer))
        {
            printer = Load(printerId);
            _printers[printerId] = printer;
        }

        return printer;
    }

    private void LoadAll()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        try
        {
            if (!Directory.Exists(_root))
            {
                return;
            }

            foreach (var folder in Directory.EnumerateDirectories(_root))
            {
                if (Guid.TryParseExact(Path.GetFileName(folder), "N", out var printerId) && !_printers.ContainsKey(printerId))
                {
                    _printers[printerId] = Load(printerId);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "The file cache in {CacheFolder} could not be read", _root);
        }
    }

    private PrinterIndex Load(Guid printerId)
    {
        var path = Path.Combine(Folder(printerId), IndexFileName);
        try
        {
            if (File.Exists(path))
            {
                var stored = JsonSerializer.Deserialize<StoredIndex>(File.ReadAllText(path), JsonOptions);
                if (stored is not null)
                {
                    return new PrinterIndex(printerId, stored.Entries.ToDictionary(e => e.Key));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A broken index is not worth keeping: start the printer's cache again.
            _logger.LogWarning(ex, "The file cache of printer {PrinterId} could not be read and is reset", printerId);
            DeleteFolder(printerId);
        }

        return new PrinterIndex(printerId, []);
    }

    private void Save(Guid printerId, PrinterIndex printer)
    {
        if (!printer.Dirty)
        {
            return;
        }

        printer.Dirty = false;
        try
        {
            if (printer.Entries.Count == 0)
            {
                DeleteFolder(printerId);
                return;
            }

            var folder = Folder(printerId);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, IndexFileName);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new StoredIndex(printer.Entries.Values.ToList()), JsonOptions));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "The file cache of printer {PrinterId} could not be saved", printerId);
        }
    }

    private void DeleteThumbnail(Guid printerId, Entry entry)
    {
        if (entry.ThumbnailFile is { } file)
        {
            try
            {
                File.Delete(Path.Combine(Folder(printerId), file));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "Cached thumbnail {ThumbnailFile} of printer {PrinterId} could not be deleted", file, printerId);
            }
        }

        entry.ThumbnailFile = null;
        entry.ThumbnailContentType = null;
        entry.ThumbnailBytes = 0;
        entry.ThumbnailKnown = false;
    }

    private void DeleteFolder(Guid printerId)
    {
        try
        {
            var folder = Folder(printerId);
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "The file cache of printer {PrinterId} could not be deleted", printerId);
        }
    }

    private string Folder(Guid printerId) => Path.Combine(_root, printerId.ToString("N"));

    // Names on the printer are compared without case, as the card's FAT file system does.
    private static string Key(string name, long size, DateTimeOffset? modifiedAt) =>
        $"{name.ToUpperInvariant()}|{size}|{modifiedAt?.UtcTicks}";

    private static string Hash(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32];

    private sealed class PrinterIndex(Guid id, Dictionary<string, Entry> entries)
    {
        public Guid Id { get; } = id;

        public Dictionary<string, Entry> Entries { get; } = entries;

        /// <summary>The files of the last listing, by name: what a request for one file is checked against.</summary>
        public Dictionary<string, (long Size, DateTimeOffset? ModifiedAt)> Listed { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public bool Dirty { get; set; }
    }

    private sealed record StoredIndex(List<Entry> Entries);

    private sealed class Entry
    {
        public required string Name { get; init; }

        public DateTimeOffset LastSeen { get; set; }

        public PrintMetadata? Metadata { get; set; }

        public bool ThumbnailKnown { get; set; }

        public string? ThumbnailFile { get; set; }

        public string? ThumbnailContentType { get; set; }

        public long ThumbnailBytes { get; set; }

        public long Size { get; init; }

        public DateTimeOffset? ModifiedAt { get; init; }

        // What an entry costs on disk: its picture, and roughly its line in the index.
        [JsonIgnore]
        public long Bytes => ThumbnailBytes + 512;

        [JsonIgnore]
        public string Key => PrinterFileCache.Key(Name, Size, ModifiedAt);
    }
}
