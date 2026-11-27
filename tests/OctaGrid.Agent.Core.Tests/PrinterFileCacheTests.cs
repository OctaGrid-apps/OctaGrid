using OctaGrid.Agent.Core.Files;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Agent.Core.Tests;

/// <summary>
/// The agent's file cache: what it keeps across restarts, when an entry no longer matches its file, and
/// cleanup after swapped cards, removed printers and printers that never return.
/// </summary>
public sealed class PrinterFileCacheTests : IDisposable
{
    private static readonly DateTimeOffset Monday = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];
    private static readonly CancellationToken Ct = CancellationToken.None;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "octagrid-file-cache-tests", Guid.NewGuid().ToString("N"));
    private readonly ManualTime _time = new(Monday);
    private readonly Guid _printer = Guid.NewGuid();

    private sealed class ManualTime : TimeProvider
    {
        public ManualTime(DateTimeOffset now)
        {
            Now = now;
        }

        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // A new instance reads what an earlier one saved, as after an agent restart.
    private PrinterFileCache Cache(long maxBytes = PrinterFileCache.DefaultMaxBytes) => new(_root, _time, maxBytes: maxBytes);

    private static PrintMetadata Listed(string name, long size = 1000) => new(name, size, null, null, null, Monday, DetailsPending: true);

    private static PrintMetadata Read(string name, long size = 1000) => new(name, size, "PLA", 0.4, 600, Monday, 12.5);

    private static PrintThumbnail Thumbnail(byte[] data) => new("image/png", data);

    private async Task ReadOnceAsync(PrinterFileCache cache, string name, long size = 1000, byte[]? thumbnail = null)
    {
        await cache.ApplyListingAsync(_printer, [Listed(name, size)], Ct);
        await cache.StoreDetailsAsync(_printer, [Read(name, size)], Ct);
        if (thumbnail is not null)
        {
            await cache.StoreThumbnailAsync(_printer, name, Thumbnail(thumbnail), Ct);
        }
    }

    [Fact]
    public async Task Details_and_thumbnails_read_before_a_restart_are_not_read_again()
    {
        await ReadOnceAsync(Cache(), "a.3mf", thumbnail: Png);

        var afterRestart = Cache();
        var listing = await afterRestart.ApplyListingAsync(_printer, [Listed("a.3mf")], Ct);

        var file = Assert.Single(listing);
        Assert.False(file.DetailsPending);
        Assert.Equal("PLA", file.MaterialType);
        Assert.Equal(Png, (await afterRestart.GetThumbnailAsync(_printer, "a.3mf", Ct))?.Data);
    }

    [Fact]
    public async Task A_file_whose_thumbnail_was_not_read_is_asked_for_again()
    {
        // Connectors return null both for a file without a thumbnail and for a failed read.
        await ReadOnceAsync(Cache(), "a.3mf", thumbnail: null);

        var cache = Cache();
        await cache.ApplyListingAsync(_printer, [Listed("a.3mf")], Ct);

        Assert.Null(await cache.GetThumbnailAsync(_printer, "a.3mf", Ct));
    }

    [Fact]
    public async Task Files_whose_names_differ_only_in_case_are_kept_apart()
    {
        var cache = Cache();
        await cache.ApplyListingAsync(_printer, [Listed("Benchy.gcode"), Listed("benchy.gcode")], Ct);
        await cache.StoreThumbnailAsync(_printer, "Benchy.gcode", Thumbnail(Png), Ct);

        var afterRestart = Cache();
        var listing = await afterRestart.ApplyListingAsync(_printer, [Listed("Benchy.gcode"), Listed("benchy.gcode")], Ct);

        Assert.Equal(2, listing.Count);
        Assert.NotNull(await afterRestart.GetThumbnailAsync(_printer, "Benchy.gcode", Ct));
        Assert.Null(await afterRestart.GetThumbnailAsync(_printer, "benchy.gcode", Ct));
    }

    [Fact]
    public async Task A_file_overwritten_with_another_size_is_read_again()
    {
        await ReadOnceAsync(Cache(), "a.3mf", size: 1000, thumbnail: Png);

        var cache = Cache();
        var listing = await cache.ApplyListingAsync(_printer, [Listed("a.3mf", size: 2000)], Ct);

        Assert.True(Assert.Single(listing).DetailsPending);
        Assert.Null(await cache.GetThumbnailAsync(_printer, "a.3mf", Ct));
    }

    [Fact]
    public async Task Putting_back_a_card_that_was_swapped_out_needs_no_reading()
    {
        var cache = Cache();
        await ReadOnceAsync(cache, "on-card-a.3mf", thumbnail: Png);

        await cache.ApplyListingAsync(_printer, [Listed("on-card-b.3mf")], Ct); // card B in
        var cardABack = await cache.ApplyListingAsync(_printer, [Listed("on-card-a.3mf")], Ct);

        Assert.False(Assert.Single(cardABack).DetailsPending);
        Assert.NotNull(await cache.GetThumbnailAsync(_printer, "on-card-a.3mf", Ct));
    }

    [Fact]
    public async Task Only_files_of_the_current_listing_answer_from_the_cache()
    {
        var cache = Cache();
        await ReadOnceAsync(cache, "a.3mf", thumbnail: Png);

        await cache.ApplyListingAsync(_printer, [Listed("b.3mf")], Ct);

        // a.3mf stays cached for when its card returns, but is not on the printer now.
        Assert.Null(await cache.GetThumbnailAsync(_printer, "a.3mf", Ct));
    }

    [Fact]
    public async Task A_print_jobs_name_that_is_not_a_listed_file_is_not_kept()
    {
        var cache = Cache();
        await cache.ApplyListingAsync(_printer, [Listed("a.3mf")], Ct);

        await cache.StoreThumbnailAsync(_printer, "rubber bolt", Thumbnail(Png), Ct);

        Assert.Null(await cache.GetThumbnailAsync(_printer, "rubber bolt", Ct));
    }

    [Fact]
    public async Task Files_no_listing_has_shown_for_thirty_days_are_forgotten()
    {
        var cache = Cache();
        await ReadOnceAsync(cache, "gone.3mf", thumbnail: Png);
        _time.Now = Monday.AddDays(20);
        await cache.ApplyListingAsync(_printer, [Listed("kept.3mf")], Ct);
        await cache.StoreDetailsAsync(_printer, [Read("kept.3mf")], Ct);

        _time.Now = Monday.AddDays(31);
        await cache.PruneAsync([_printer], Ct);

        var listing = await Cache().ApplyListingAsync(_printer, [Listed("gone.3mf"), Listed("kept.3mf")], Ct);
        Assert.True(listing.Single(f => f.FileName == "gone.3mf").DetailsPending);
        Assert.False(listing.Single(f => f.FileName == "kept.3mf").DetailsPending);
    }

    [Fact]
    public async Task A_printer_that_never_connects_again_loses_its_cache_and_folder()
    {
        await ReadOnceAsync(Cache(), "a.3mf", thumbnail: Png);
        Assert.True(Directory.Exists(PrinterFolder()));

        // Still configured, but not listed for longer than the age limit.
        _time.Now = Monday.AddDays(31);
        await Cache().PruneAsync([_printer], Ct);

        Assert.False(Directory.Exists(PrinterFolder()));
    }

    [Fact]
    public async Task A_printer_that_is_no_longer_the_agents_loses_its_cache_at_once()
    {
        var other = Guid.NewGuid();
        var cache = Cache();
        await ReadOnceAsync(cache, "a.3mf", thumbnail: Png);
        await cache.ApplyListingAsync(other, [Listed("b.3mf")], Ct);
        await cache.StoreDetailsAsync(other, [Read("b.3mf")], Ct);

        await Cache().PruneAsync([other], Ct);

        Assert.False(Directory.Exists(PrinterFolder()));
        Assert.True(Directory.Exists(Path.Combine(_root, other.ToString("N"))));
    }

    [Fact]
    public async Task Past_the_size_limit_the_files_seen_longest_ago_go_first()
    {
        var cache = Cache(maxBytes: 3000);
        var picture = new byte[1000];
        await ReadOnceAsync(cache, "old.3mf", thumbnail: picture);
        _time.Now = Monday.AddDays(1);
        await cache.ApplyListingAsync(_printer, [Listed("old.3mf"), Listed("new.3mf")], Ct);
        _time.Now = Monday.AddDays(2);
        await cache.ApplyListingAsync(_printer, [Listed("new.3mf")], Ct);
        await cache.StoreDetailsAsync(_printer, [Read("new.3mf")], Ct);

        await cache.StoreThumbnailAsync(_printer, "new.3mf", Thumbnail(picture), Ct); // past the limit

        await cache.ApplyListingAsync(_printer, [Listed("old.3mf"), Listed("new.3mf")], Ct);
        Assert.Null(await cache.GetThumbnailAsync(_printer, "old.3mf", Ct));
        Assert.NotNull(await cache.GetThumbnailAsync(_printer, "new.3mf", Ct));
    }

    [Fact]
    public async Task A_damaged_cache_is_started_again_instead_of_failing()
    {
        await ReadOnceAsync(Cache(), "a.3mf", thumbnail: Png);
        await File.WriteAllTextAsync(Path.Combine(PrinterFolder(), "index.json"), "{ not json", Ct);

        var listing = await Cache().ApplyListingAsync(_printer, [Listed("a.3mf")], Ct);

        Assert.True(Assert.Single(listing).DetailsPending);
    }

    [Fact]
    public async Task A_cache_folder_that_cannot_be_written_does_not_stop_the_listing()
    {
        // A file in place of the cache folder makes every write fail.
        Directory.CreateDirectory(Path.GetDirectoryName(_root)!);
        await File.WriteAllTextAsync(_root, "in the way", Ct);
        try
        {
            var cache = Cache();
            var listing = await cache.ApplyListingAsync(_printer, [Listed("a.3mf")], Ct);
            await cache.StoreDetailsAsync(_printer, [Read("a.3mf")], Ct);
            await cache.StoreThumbnailAsync(_printer, "a.3mf", Thumbnail(Png), Ct);

            Assert.Single(listing);
        }
        finally
        {
            File.Delete(_root);
        }
    }

    private string PrinterFolder() => Path.Combine(_root, _printer.ToString("N"));
}
