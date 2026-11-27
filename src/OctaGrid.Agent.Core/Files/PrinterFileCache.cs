using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Agent.Core.Files;

/// <summary>
/// File details and thumbnails read from printers, kept on disk across restarts for every connector.
/// An entry is valid while the file's name, size and modified time match the ones it was read with.
/// <para>
/// Entries outlive their file's listing, so a card that is swapped back in is not read again. An entry is
/// removed after <c>maxAge</c> without a listing showing it, oldest first when the total passes
/// <c>maxBytes</c>, and with its printer's folder when the printer is no longer the agent's.
/// </para>
/// Best effort: disk errors are logged and the file is read from the printer instead.
/// </summary>
public sealed class PrinterFileCache
{
    /// <summary>How long an entry is kept after the last listing that showed its file.</summary>
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromDays(30);

    /// <summary>The most the cache keeps on disk, details and thumbnails together.</summary>
    public const long DefaultMaxBytes = 256L * 1024 * 1024;

    private const string IndexFileName = "index.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim _gate = new(1, 1);
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

    /// <summary>The default folder: <c>%LocalAppData%/OctaGrid.Data/file-cache</c>.</summary>
    public static string DefaultRoot { get; } = Path.Combine(AgentLocalData.Root, "file-cache");

    /// <summary>
    /// Records a printer's file listing and returns it with cached details filled in. Details the listing
    /// itself carries are cached.
    /// </summary>
    public async Task<IReadOnlyList<PrintMetadata>> ApplyListingAsync(
        Guid printerId, IReadOnlyList<PrintMetadata> listing, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var printer = await PrinterAsync(printerId, cancellationToken);
            var now = _time.GetUtcNow();

            // Indexer, not ToDictionary: a name listed twice must not fail the listing.
            printer.Listed = new Dictionary<string, (long, DateTimeOffset?)>(listing.Count, StringComparer.Ordinal);
            foreach (var file in listing)
            {
                printer.Listed[file.FileName] = (file.FileSize, file.ModifiedAt);
            }

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
            await SaveAsync(printer, cancellationToken);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Caches the details of files in the last listing; other files are ignored.</summary>
    public async Task StoreDetailsAsync(
        Guid printerId, IReadOnlyList<PrintMetadata> details, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var printer = await PrinterAsync(printerId, cancellationToken);
            foreach (var file in details.Where(f => !f.DetailsPending))
            {
                if (EntryOfListed(printer, file.FileName) is { } entry)
                {
                    entry.Metadata = file;
                    printer.Dirty = true;
                }
            }

            await SaveAsync(printer, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The cached thumbnail of a file in the last listing, or null when none is cached.</summary>
    public async Task<PrintThumbnail?> GetThumbnailAsync(
        Guid printerId, string fileName, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var printer = await PrinterAsync(printerId, cancellationToken);
            if (EntryOfListed(printer, fileName) is not { ThumbnailFile: { } file } entry)
            {
                return null;
            }

            try
            {
                var data = await File.ReadAllBytesAsync(Path.Combine(Folder(printerId), file), cancellationToken);
                return new PrintThumbnail(entry.ThumbnailContentType ?? "image/png", data);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "Cached thumbnail of '{FileName}' on printer {PrinterId} could not be read",
                    fileName, printerId);
                entry.ThumbnailFile = null;
                entry.ThumbnailBytes = 0;
                printer.Dirty = true;
                return null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Caches the thumbnail of a file in the last listing. A missing thumbnail is never cached: connectors
    /// also return null when the read failed.
    /// </summary>
    public async Task StoreThumbnailAsync(
        Guid printerId, string fileName, PrintThumbnail thumbnail, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(thumbnail);
        if (thumbnail.Data.Length == 0)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var printer = await PrinterAsync(printerId, cancellationToken);
            if (EntryOfListed(printer, fileName) is not { } entry)
            {
                return;
            }

            try
            {
                DeleteThumbnail(printerId, entry);
                Directory.CreateDirectory(Folder(printerId));
                var file = $"{Hash(entry.Key)}.img";
                await File.WriteAllBytesAsync(Path.Combine(Folder(printerId), file), thumbnail.Data, cancellationToken);
                entry.ThumbnailFile = file;
                entry.ThumbnailContentType = thumbnail.ContentType;
                entry.ThumbnailBytes = thumbnail.Data.Length;
                printer.Dirty = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "Thumbnail of '{FileName}' on printer {PrinterId} could not be cached",
                    fileName, printerId);
            }

            await SaveAsync(printer, cancellationToken);
            await EnforceSizeLimitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Removes the cache of printers not in <paramref name="printers"/> and entries past their age limit.
    /// </summary>
    /// <param name="printers">All of the agent's printers, enabled or not.</param>
    /// <param name="cancellationToken">Cancels the cleanup; what is done so far stays done.</param>
    public async Task PruneAsync(IReadOnlyCollection<Guid> printers, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await LoadAllAsync(cancellationToken);
            var keep = printers.ToHashSet();
            foreach (var printerId in _printers.Keys.Where(id => !keep.Contains(id)).ToList())
            {
                _logger.LogInformation("Removing the file cache of printer {PrinterId}: not one of the agent's printers",
                    printerId);
                _printers.Remove(printerId);
                DeleteFolder(printerId);
            }

            var now = _time.GetUtcNow();
            foreach (var printer in _printers.Values.ToList())
            {
                RemoveExpired(printer, now);
                await SaveAsync(printer, cancellationToken);
            }

            await EnforceSizeLimitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void RemoveExpired(PrinterIndex printer, DateTimeOffset now)
    {
        foreach (var (key, entry) in printer.Entries.Where(e => now - e.Value.LastSeen > _maxAge).ToList())
        {
            RemoveEntry(printer, key, entry);
        }
    }

    private async Task EnforceSizeLimitAsync(CancellationToken cancellationToken)
    {
        var total = _printers.Values.Sum(p => p.Entries.Values.Sum(e => e.Bytes));
        if (total <= _maxBytes)
        {
            return;
        }

        var oldestFirst = _printers.Values
            .SelectMany(p => p.Entries.Select(e => (Printer: p, e.Key, Entry: e.Value)))
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

        foreach (var printer in _printers.Values.ToList())
        {
            await SaveAsync(printer, cancellationToken);
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

    private async Task<PrinterIndex> PrinterAsync(Guid printerId, CancellationToken cancellationToken)
    {
        if (!_printers.TryGetValue(printerId, out var printer))
        {
            printer = await LoadAsync(printerId, cancellationToken);
            _printers[printerId] = printer;
        }

        return printer;
    }

    private async Task LoadAllAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return;
        }

        try
        {
            if (Directory.Exists(_root))
            {
                foreach (var folder in Directory.EnumerateDirectories(_root))
                {
                    if (Guid.TryParseExact(Path.GetFileName(folder), "N", out var printerId))
                    {
                        await PrinterAsync(printerId, cancellationToken);
                    }
                }
            }

            _loaded = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "The file cache in {CacheFolder} could not be read", _root);
        }
    }

    private async Task<PrinterIndex> LoadAsync(Guid printerId, CancellationToken cancellationToken)
    {
        var path = Path.Combine(Folder(printerId), IndexFileName);
        try
        {
            if (File.Exists(path))
            {
                await using var stream = File.OpenRead(path);
                var stored = await JsonSerializer.DeserializeAsync<StoredIndex>(stream, JsonOptions, cancellationToken);
                if (stored is not null)
                {
                    // Indexer, not ToDictionary: an index from an older key format may repeat a key.
                    var entries = new Dictionary<string, Entry>(stored.Entries.Count);
                    foreach (var entry in stored.Entries)
                    {
                        entries[entry.Key] = entry;
                    }

                    return new PrinterIndex(printerId, entries);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "The file cache of printer {PrinterId} could not be read and is reset", printerId);
            DeleteFolder(printerId);
        }

        return new PrinterIndex(printerId, []);
    }

    private async Task SaveAsync(PrinterIndex printer, CancellationToken cancellationToken)
    {
        if (!printer.Dirty)
        {
            return;
        }

        try
        {
            if (printer.Entries.Count == 0)
            {
                DeleteFolder(printer.Id);
            }
            else
            {
                var folder = Folder(printer.Id);
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, IndexFileName);
                var temp = path + ".tmp";

                // Written aside and moved, so a crash never leaves a half-written index.
                await using (var stream = File.Create(temp))
                {
                    await JsonSerializer.SerializeAsync(
                        stream, new StoredIndex(printer.Entries.Values.ToList()), JsonOptions, cancellationToken);
                }

                File.Move(temp, path, overwrite: true);
            }

            printer.Dirty = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "The file cache of printer {PrinterId} could not be saved", printer.Id);
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
                _logger.LogDebug(ex, "Cached thumbnail {ThumbnailFile} of printer {PrinterId} could not be deleted",
                    file, printerId);
            }
        }

        entry.ThumbnailFile = null;
        entry.ThumbnailContentType = null;
        entry.ThumbnailBytes = 0;
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

    // Case-sensitive: Klipper's file system can hold both "A.gcode" and "a.gcode".
    private static string Key(string name, long size, DateTimeOffset? modifiedAt) =>
        $"{name}|{size}|{modifiedAt?.UtcTicks}";

    private static string Hash(string key) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32];

    private sealed class PrinterIndex
    {
        public PrinterIndex(Guid id, Dictionary<string, Entry> entries)
        {
            Id = id;
            Entries = entries;
        }

        public Guid Id { get; }

        public Dictionary<string, Entry> Entries { get; }

        /// <summary>Files of the last listing by name, for checking single-file requests.</summary>
        public Dictionary<string, (long Size, DateTimeOffset? ModifiedAt)> Listed { get; set; } =
            new(StringComparer.Ordinal);

        public bool Dirty { get; set; }
    }

    private sealed record StoredIndex(List<Entry> Entries);

    private sealed class Entry
    {
        public required string Name { get; init; }

        public DateTimeOffset LastSeen { get; set; }

        public PrintMetadata? Metadata { get; set; }

        public string? ThumbnailFile { get; set; }

        public string? ThumbnailContentType { get; set; }

        public long ThumbnailBytes { get; set; }

        public long Size { get; init; }

        public DateTimeOffset? ModifiedAt { get; init; }

        // Size on disk: the thumbnail plus an estimate for the entry's share of the index.
        [JsonIgnore]
        public long Bytes => ThumbnailBytes + 512;

        [JsonIgnore]
        public string Key => PrinterFileCache.Key(Name, Size, ModifiedAt);
    }
}
