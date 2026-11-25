using System.Globalization;
using OctaGrid.Client.Services;
using OctaGrid.Client.ViewModels;
using OctaGrid.Extensions.Models;
using OctaGrid.Extensions.Models.Exceptions;

namespace OctaGrid.Client.Tests;

/// <summary>The list of files on a printer: its order, what each file shows and how its thumbnails load.</summary>
public class FileListTests
{
    private static readonly DateTimeOffset Today = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    private static PrintMetadata File(string name, long size = 1024, int daysAgo = 0) =>
        new(name, size, "PLA", 0.4, 600, Today.AddDays(-daysAgo));

    private static FilePickerViewModel Picker(PrinterPageTabsTests.FakeCommands commands, IByteArrayImageCache? cache = null, Guid? printerId = null) =>
        new(printerId ?? Guid.NewGuid(), commands, cache ?? new ByteArrayImageCache());

    private static List<string> Names(FilePickerViewModel picker) => picker.Files.Select(f => f.Metadata.FileName).ToList();

    [Fact]
    public async Task Files_are_listed_newest_first_like_the_printer_lists_them()
    {
        var commands = new PrinterPageTabsTests.FakeCommands();
        commands.Files.AddRange([File("old.3mf", daysAgo: 30), File("undated.3mf") with { ModifiedAt = null }, File("new.3mf", daysAgo: 1)]);
        var picker = Picker(commands);

        await picker.LoadDataCommand.ExecuteAsync(null);

        Assert.Equal(FileSortOrder.NewestFirst, picker.SelectedSort.Order);
        Assert.Equal(["new.3mf", "old.3mf", "undated.3mf"], Names(picker));
    }

    [Theory]
    [InlineData(FileSortOrder.NameAscending, new[] { "plate_2.3mf", "Plate_10.3mf", "z.3mf" })]
    [InlineData(FileSortOrder.NameDescending, new[] { "z.3mf", "Plate_10.3mf", "plate_2.3mf" })]
    [InlineData(FileSortOrder.LargestFirst, new[] { "z.3mf", "plate_2.3mf", "Plate_10.3mf" })]
    [InlineData(FileSortOrder.SmallestFirst, new[] { "Plate_10.3mf", "plate_2.3mf", "z.3mf" })]
    [InlineData(FileSortOrder.OldestFirst, new[] { "plate_2.3mf", "z.3mf", "Plate_10.3mf" })]
    public async Task Files_can_be_sorted_by_name_size_and_age(FileSortOrder order, string[] expected)
    {
        var commands = new PrinterPageTabsTests.FakeCommands();
        commands.Files.AddRange([File("Plate_10.3mf", size: 10, daysAgo: 1), File("z.3mf", size: 3000, daysAgo: 2), File("plate_2.3mf", size: 200, daysAgo: 3)]);
        var picker = Picker(commands);
        await picker.LoadDataCommand.ExecuteAsync(null);

        picker.SelectedSort = FileSortOption.All.Single(o => o.Order == order);

        Assert.Equal(expected, Names(picker));
    }

    [Fact]
    public async Task Sorting_again_keeps_the_files_and_their_thumbnails()
    {
        var commands = new PrinterPageTabsTests.FakeCommands();
        commands.Files.AddRange([File("a.3mf", size: 1), File("b.3mf", size: 2)]);
        var picker = Picker(commands);
        await picker.LoadDataCommand.ExecuteAsync(null);
        var before = picker.Files.ToList();

        picker.SelectedSort = FileSortOption.All.Single(o => o.Order == FileSortOrder.LargestFirst);

        Assert.Equal(before.OrderBy(f => f.Metadata.FileName), picker.Files.OrderBy(f => f.Metadata.FileName));
    }

    [Fact]
    public async Task The_list_is_shown_before_its_thumbnails_arrive()
    {
        var commands = new PrinterPageTabsTests.FakeCommands();
        commands.Files.Add(File("a.3mf"));
        var thumbnail = new TaskCompletionSource<PrintThumbnail?>(TaskCreationOptions.RunContinuationsAsynchronously);
        commands.Thumbnails = (_, _) => thumbnail.Task;
        var picker = Picker(commands);

        await picker.LoadDataCommand.ExecuteAsync(null);

        Assert.False(picker.IsFilesLoading);
        Assert.Empty(picker.Files.Single().ThumbnailData);

        thumbnail.SetResult(new PrintThumbnail("image/png", [7]));
        await picker.ThumbnailsLoading;
        Assert.Equal([7], picker.Files.Single().ThumbnailData);
    }

    [Fact]
    public async Task At_most_three_thumbnails_are_asked_for_at_once()
    {
        var commands = new PrinterPageTabsTests.FakeCommands();
        commands.Files.AddRange(Enumerable.Range(0, 10).Select(i => File($"{i}.3mf")));
        var open = 0;
        var mostOpen = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        commands.Thumbnails = async (_, _) =>
        {
            var now = Interlocked.Increment(ref open);
            InterlockedMax(ref mostOpen, now);
            await release.Task;
            Interlocked.Decrement(ref open);
            return null;
        };
        var picker = Picker(commands);

        await picker.LoadDataCommand.ExecuteAsync(null);
        release.SetResult();
        await picker.ThumbnailsLoading;

        Assert.Equal(3, mostOpen);
    }

    [Fact]
    public async Task Closing_the_list_stops_asking_for_thumbnails()
    {
        var commands = new PrinterPageTabsTests.FakeCommands();
        commands.Files.AddRange([File("a.3mf"), File("b.3mf"), File("c.3mf"), File("d.3mf")]);
        var asked = new List<string>();
        commands.Thumbnails = async (name, ct) =>
        {
            lock (asked)
            {
                asked.Add(name);
            }

            await Task.Delay(Timeout.Infinite, ct);
            return null;
        };
        var picker = Picker(commands);
        await picker.LoadDataCommand.ExecuteAsync(null);

        picker.StopLoadingThumbnails();
        await picker.ThumbnailsLoading;

        Assert.Equal(3, asked.Count); // the fourth was never asked for
    }

    [Fact]
    public async Task Two_printers_with_a_file_of_the_same_name_keep_their_own_thumbnails()
    {
        var cache = new ByteArrayImageCache();
        var first = new PrinterPageTabsTests.FakeCommands { Thumbnails = (_, _) => Task.FromResult<PrintThumbnail?>(new("image/png", [1])) };
        var second = new PrinterPageTabsTests.FakeCommands { Thumbnails = (_, _) => Task.FromResult<PrintThumbnail?>(new("image/png", [2])) };
        first.Files.Add(File("plate.3mf"));
        second.Files.Add(File("plate.3mf"));
        var firstPicker = Picker(first, cache);
        var secondPicker = Picker(second, cache);

        await firstPicker.LoadDataCommand.ExecuteAsync(null);
        await firstPicker.ThumbnailsLoading;
        await secondPicker.LoadDataCommand.ExecuteAsync(null);
        await secondPicker.ThumbnailsLoading;

        Assert.Equal([2], secondPicker.Files.Single().ThumbnailData);
    }

    [Fact]
    public async Task A_printer_without_an_SD_card_says_so()
    {
        const string message = "This printer has no SD card. Files in its internal storage can only be started from the printer's screen.";
        var commands = new PrinterPageTabsTests.FakeCommands { FilesFailure = new StorageUnavailableException(message) };
        var picker = Picker(commands);

        await picker.LoadDataCommand.ExecuteAsync(null);

        Assert.Equal(message, picker.ErrorMessage);
        Assert.False(picker.IsEmpty);
    }

    [Fact]
    public void A_file_shows_its_size_weight_and_date_in_the_users_format()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-GB");
            var file = new PrintMetadata("a.3mf", 4_404_019, "PLA", 0.4, 5400, Today, 12.34);
            var item = new PrintMetadataListItem(file, []);

            Assert.Equal("PLA • 0.4 mm • 12.3 g", item.DetailsText);
            Assert.Equal("4.2 MB", item.SizeText);
            Assert.Equal(Today.ToLocalTime().ToString("d", CultureInfo.CurrentCulture), item.DateText);
            Assert.Equal($"PLA • 0.4 mm • 12.3 g • 1h 30m • 4.2 MB • {item.DateText}", item.SummaryText);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(150L * 1024 * 1024, "150 MB")]
    [InlineData(5L * 1024 * 1024 * 1024, "5 GB")]
    public void Sizes_are_shown_as_people_read_them(long bytes, string expected)
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Assert.Equal(expected, PrintFileText.Size(bytes));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void A_file_whose_slicer_records_nothing_shows_no_empty_separators()
    {
        var item = new PrintMetadataListItem(new PrintMetadata("a.3mf", 10, null, null, null), []);

        Assert.Equal(string.Empty, item.DetailsText);
        Assert.Null(item.DateText);
        Assert.DoesNotContain("•", item.SummaryText, StringComparison.Ordinal);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        var current = Volatile.Read(ref target);
        while (value > current)
        {
            var seen = Interlocked.CompareExchange(ref target, value, current);
            if (seen == current)
            {
                return;
            }

            current = seen;
        }
    }
}
