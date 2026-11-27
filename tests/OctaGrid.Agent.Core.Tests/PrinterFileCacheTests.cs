using OctaGrid.Agent.Core.Files;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Agent.Core.Tests;

/// <summary>
/// The agent's file cache: what it keeps across restarts, when an entry no longer belongs to the file, and how it
/// cleans up after swapped cards, removed printers and printers that never come back.
/// </summary>
public sealed class PrinterFileCacheTests : IDisposable
{
    private static readonly DateTimeOffset Monday = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "octagrid-file-cache-tests", Guid.NewGuid().ToString("N"));
    private readonly ManualTime _time = new(Monday);
    private readonly Guid _printer = Guid.NewGuid();

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // A new instance reads what an earlier one saved, as the agent does after a restart.
    private PrinterFileCache Cache(long maxBytes = PrinterFileCache.DefaultMaxBytes) => new(_root, _time, maxBytes: maxBytes);

    private static PrintMetadata Listed(string name, long size = 1000) => new(name, size, null, null, null, Monday, DetailsPending: true);

    private static PrintMetadata Read(string name, long size = 1000) => new(name, size, "PLA", 0.4, 600, Monday, 12.5);

    private void ReadOnce(PrinterFileCache cache, string name, long size = 1000, byte[]? thumbnail = null)
    {
        cache.ApplyListing(_printer, [Listed(name, size)]);
        cache.StoreDetails(_printer, [Read(name, size)]);
        cache.StoreThumbnail(_printer, name, thumbnail is null ? null : new PrintThumbnail("image/png", thumbnail));
    }

    [Fact]
    public void Details_and_thumbnails_read_before_a_restart_are_not_read_again()
    {
        ReadOnce(Cache(), "a.3mf", thumbnail: Png);

        var afterRestart = Cache();
        var listing = afterRestart.ApplyListing(_printer, [Listed("a.3mf")]);

        var file = Assert.Single(listing);
        Assert.False(file.DetailsPending);
        Assert.Equal("PLA", file.MaterialType);
        Assert.True(afterRestart.TryGetThumbnail(_printer, "a.3mf", out var thumbnail));
        Assert.Equal(Png, thumbnail!.Data);
    }

    [Fact]
    public void A_file_known_to_have_no_thumbnail_is_not_asked_for_again()
    {
        ReadOnce(Cache(), "a.3mf", thumbnail: null);

        var cache = Cache();
        cache.ApplyListing(_printer, [Listed("a.3mf")]);

        Assert.True(cache.TryGetThumbnail(_printer, "a.3mf", out var thumbnail));
        Assert.Null(thumbnail);
    }

    [Fact]
    public void A_file_overwritten_with_another_size_is_read_again()
    {
        ReadOnce(Cache(), "a.3mf", size: 1000, thumbnail: Png);

        var cache = Cache();
        var listing = cache.ApplyListing(_printer, [Listed("a.3mf", size: 2000)]);

        Assert.True(Assert.Single(listing).DetailsPending);
        Assert.False(cache.TryGetThumbnail(_printer, "a.3mf", out _));
    }

    [Fact]
    public void Putting_back_a_card_that_was_swapped_out_needs_no_reading()
    {
        var cache = Cache();
        ReadOnce(cache, "on-card-a.3mf", thumbnail: Png);

        cache.ApplyListing(_printer, [Listed("on-card-b.3mf")]); // card B in
        var cardABack = cache.ApplyListing(_printer, [Listed("on-card-a.3mf")]);

        Assert.False(Assert.Single(cardABack).DetailsPending);
        Assert.True(cache.TryGetThumbnail(_printer, "on-card-a.3mf", out var thumbnail));
        Assert.NotNull(thumbnail);
    }

    [Fact]
    public void Only_files_of_the_current_listing_answer_from_the_cache()
    {
        var cache = Cache();
        ReadOnce(cache, "a.3mf", thumbnail: Png);

        cache.ApplyListing(_printer, [Listed("b.3mf")]);

        // a.3mf is kept for when its card returns, but it is not on the printer now.
        Assert.False(cache.TryGetThumbnail(_printer, "a.3mf", out _));
    }

    [Fact]
    public void A_print_jobs_name_that_is_not_a_listed_file_is_not_kept()
    {
        var cache = Cache();
        cache.ApplyListing(_printer, [Listed("a.3mf")]);

        cache.StoreThumbnail(_printer, "rubber bolt", new PrintThumbnail("image/png", Png));

        Assert.False(cache.TryGetThumbnail(_printer, "rubber bolt", out _));
    }

    [Fact]
    public void Files_no_listing_has_shown_for_thirty_days_are_forgotten()
    {
        var cache = Cache();
        ReadOnce(cache, "gone.3mf", thumbnail: Png);
        _time.Now = Monday.AddDays(20);
        cache.ApplyListing(_printer, [Listed("kept.3mf")]);
        cache.StoreDetails(_printer, [Read("kept.3mf")]);

        _time.Now = Monday.AddDays(31);
        cache.Prune([_printer]);

        var listing = Cache().ApplyListing(_printer, [Listed("gone.3mf"), Listed("kept.3mf")]);
        Assert.True(listing.Single(f => f.FileName == "gone.3mf").DetailsPending);
        Assert.False(listing.Single(f => f.FileName == "kept.3mf").DetailsPending);
    }

    [Fact]
    public void A_printer_that_never_connects_again_loses_its_cache_and_folder()
    {
        ReadOnce(Cache(), "a.3mf", thumbnail: Png);
        Assert.True(Directory.Exists(PrinterFolder()));

        // Still configured, but no listing for longer than the cache keeps anything.
        _time.Now = Monday.AddDays(31);
        Cache().Prune([_printer]);

        Assert.False(Directory.Exists(PrinterFolder()));
    }

    [Fact]
    public void A_printer_that_is_no_longer_the_agents_loses_its_cache_at_once()
    {
        var other = Guid.NewGuid();
        var cache = Cache();
        ReadOnce(cache, "a.3mf", thumbnail: Png);
        cache.ApplyListing(other, [Listed("b.3mf")]);
        cache.StoreDetails(other, [Read("b.3mf")]);

        Cache().Prune([other]);

        Assert.False(Directory.Exists(PrinterFolder()));
        Assert.True(Directory.Exists(Path.Combine(_root, other.ToString("N"))));
    }

    [Fact]
    public void Past_the_size_limit_the_files_seen_longest_ago_go_first()
    {
        var cache = Cache(maxBytes: 3000);
        var picture = new byte[1000];
        ReadOnce(cache, "old.3mf", thumbnail: picture);
        _time.Now = Monday.AddDays(1);
        cache.ApplyListing(_printer, [Listed("old.3mf"), Listed("new.3mf")]);
        _time.Now = Monday.AddDays(2);
        cache.ApplyListing(_printer, [Listed("new.3mf")]);
        cache.StoreDetails(_printer, [Read("new.3mf")]);

        cache.StoreThumbnail(_printer, "new.3mf", new PrintThumbnail("image/png", picture)); // past the limit

        cache.ApplyListing(_printer, [Listed("old.3mf"), Listed("new.3mf")]);
        Assert.False(cache.TryGetThumbnail(_printer, "old.3mf", out _));
        Assert.True(cache.TryGetThumbnail(_printer, "new.3mf", out var kept));
        Assert.NotNull(kept);
    }

    [Fact]
    public void A_damaged_cache_is_started_again_instead_of_failing()
    {
        ReadOnce(Cache(), "a.3mf", thumbnail: Png);
        File.WriteAllText(Path.Combine(PrinterFolder(), "index.json"), "{ not json");

        var listing = Cache().ApplyListing(_printer, [Listed("a.3mf")]);

        Assert.True(Assert.Single(listing).DetailsPending);
    }

    [Fact]
    public void A_cache_folder_that_cannot_be_written_does_not_stop_the_listing()
    {
        // A file where the cache wants its folder: every write fails.
        Directory.CreateDirectory(Path.GetDirectoryName(_root)!);
        File.WriteAllText(_root, "in the way");
        try
        {
            var cache = Cache();
            var listing = cache.ApplyListing(_printer, [Listed("a.3mf")]);
            cache.StoreDetails(_printer, [Read("a.3mf")]);
            cache.StoreThumbnail(_printer, "a.3mf", new PrintThumbnail("image/png", Png));

            Assert.Single(listing);
        }
        finally
        {
            File.Delete(_root);
        }
    }

    private string PrinterFolder() => Path.Combine(_root, _printer.ToString("N"));
}
