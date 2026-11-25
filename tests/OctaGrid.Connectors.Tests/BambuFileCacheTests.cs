using OctaGrid.Connectors.BambuLab;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Connectors.Tests;

public class BambuFileCacheTests
{
    private static readonly DateTimeOffset Monday = new(2026, 11, 2, 9, 30, 0, TimeSpan.Zero);

    private static BambuFileCache.Entry Entry(long size, DateTimeOffset? modifiedAt) =>
        new(size, modifiedAt, new PrintMetadata("a.gcode.3mf", size, "PLA", 0.4, 60, modifiedAt), [1, 2, 3]);

    [Fact]
    public void A_file_read_before_is_found_while_its_size_and_time_are_the_same()
    {
        var cache = new BambuFileCache();
        cache.Store("a.gcode.3mf", Entry(100, Monday));

        Assert.True(cache.TryGet("a.gcode.3mf", 100, Monday, out var entry));
        Assert.Equal([1, 2, 3], entry.Thumbnail);
    }

    [Theory]
    [InlineData(101, 0)]
    [InlineData(100, 1)]
    public void A_file_that_changed_is_read_again(long size, int minutesLater)
    {
        var cache = new BambuFileCache();
        cache.Store("a.gcode.3mf", Entry(100, Monday));

        Assert.False(cache.TryGet("a.gcode.3mf", size, Monday.AddMinutes(minutesLater), out _));
    }

    [Fact]
    public void A_listing_forgets_the_files_that_are_gone()
    {
        var cache = new BambuFileCache();
        cache.Store("a.gcode.3mf", Entry(100, Monday));
        cache.Store("b.gcode.3mf", Entry(200, Monday));

        cache.SetListing(["b.gcode.3mf"]);

        Assert.False(cache.TryGet("a.gcode.3mf", out _));
        Assert.True(cache.TryGet("b.gcode.3mf", out _));
        Assert.Equal(["b.gcode.3mf"], cache.ListedNames);
    }

    [Theory]
    [InlineData("480g VD P2S.Husky down platform_PETG_23h49m", "480g VD P2S.Husky down platform_PETG_23h49m.gcode.3mf")]
    [InlineData("rubber bolt", "rubber bolt_TPU_40m26s.gcode.3mf")]
    [InlineData("KOIN-DOC", "KOIN-DOC.3mf")]
    [InlineData("koin-doc.3mf", "KOIN-DOC.3mf")]
    public void A_print_job_is_matched_to_its_file_on_the_card(string jobName, string expected)
    {
        string[] files =
        [
            "480g VD P2S.Husky down platform_PETG_23h49m.gcode.3mf",
            "rubber bolt_TPU_40m26s.gcode.3mf",
            "KOIN-DOC.3mf",
            "KOIN-DOC_PLA_7h18m.gcode.3mf",
        ];

        Assert.Equal(expected, BambuFileCache.ResolveJobFile(jobName, files));
    }

    [Theory]
    [InlineData("project_file")] // sent from Bambu Studio: not on the card
    [InlineData("Phone Holder")] // two exports of it: either could be the job's
    [InlineData("")]
    public void A_print_job_without_one_clear_file_has_none(string jobName)
    {
        string[] files = ["Phone Holder_PETG_12h19m.gcode.3mf", "Phone Holder_PETG_4h31m.gcode.3mf", "project_file.stl"];

        Assert.Null(BambuFileCache.ResolveJobFile(jobName, files));
    }
}
