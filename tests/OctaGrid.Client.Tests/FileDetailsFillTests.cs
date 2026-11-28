using OctaGrid.Client.Models;
using OctaGrid.Client.Services;
using OctaGrid.Client.ViewModels;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Client.Tests;

/// <summary>
/// A printer whose listing leaves the slicer's details out: the list shows at once, and the details, then the
/// thumbnails, fill in from the top of the list down.
/// </summary>
public class FileDetailsFillTests
{
    private static readonly DateTimeOffset Today = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    // As the listing returns a file the connector has not read yet: name, size and date only.
    private static PrintMetadata Listed(string name, int daysAgo = 0) =>
        new(name, 1024, null, null, null, Today.AddDays(-daysAgo), DetailsPending: true);

    private static FilePickerViewModel Picker(PrinterPageTabsTests.FakeCommands commands) =>
        new(Guid.NewGuid(), commands, new ByteArrayImageCache());

    [Fact]
    public async Task The_list_shows_before_the_details_are_read_and_says_they_are_coming()
    {
        var commands = new PrinterPageTabsTests.FakeCommands();
        commands.Files.Add(Listed("a.3mf"));
        var details = new TaskCompletionSource<IReadOnlyList<PrintMetadata>>(TaskCreationOptions.RunContinuationsAsynchronously);
        commands.Details = (_, _) => details.Task;
        var picker = Picker(commands);

        await picker.LoadDataCommand.ExecuteAsync(null);

        var item = Assert.Single(picker.Files);
        Assert.False(picker.IsFilesLoading);
        Assert.Equal(PrintFileText.ReadingDetails, item.DetailsText);
        Assert.Equal("1 KB", item.SizeText);

        details.SetResult([new PrintMetadata("a.3mf", 1024, "PETG", 0.4, 600, Today, 12)]);
        await picker.ThumbnailsLoading;
        Assert.False(item.Metadata.DetailsPending);
        Assert.StartsWith("PETG", item.DetailsText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Details_are_asked_for_from_the_top_of_the_list_a_few_files_at_a_time()
    {
        var commands = new PrinterPageTabsTests.FakeCommands();
        commands.Files.AddRange(Enumerable.Range(0, 20).Select(i => Listed($"{i:00}.3mf", daysAgo: i)));
        var picker = Picker(commands);

        await picker.LoadDataCommand.ExecuteAsync(null);
        await picker.ThumbnailsLoading;

        Assert.Equal([8, 8, 4], commands.DetailRequests.Select(r => r.Count));
        Assert.Equal(Enumerable.Range(0, 8).Select(i => $"{i:00}.3mf"), commands.DetailRequests[0]); // newest first
        Assert.All(picker.Files, f => Assert.False(f.Metadata.DetailsPending));
    }

    [Fact]
    public async Task Sorting_while_details_load_moves_the_next_batch_to_the_new_top_of_the_list()
    {
        var commands = new PrinterPageTabsTests.FakeCommands();
        commands.Files.AddRange(Enumerable.Range(0, 20).Select(i => Listed($"{i:00}.3mf", daysAgo: i)));
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        commands.Details = async (names, _) =>
        {
            if (commands.DetailRequests.Count == 1)
            {
                await first.Task;
            }

            return names.Select(n => commands.Files.Single(f => f.FileName == n) with { DetailsPending = false }).ToList();
        };
        var picker = Picker(commands);
        await picker.LoadDataCommand.ExecuteAsync(null);

        picker.SelectedSort = FileSortOption.All.Single(o => o.Order == FileSortOrder.OldestFirst);
        first.SetResult();
        await picker.ThumbnailsLoading;

        // The newest eight were asked for before the change; next come the oldest of the rest.
        Assert.Equal(Enumerable.Range(12, 8).Reverse().Select(i => $"{i:00}.3mf"), commands.DetailRequests[1]);
    }

    [Fact]
    public async Task A_thumbnail_is_asked_for_only_after_its_files_details_arrived()
    {
        var commands = new PrinterPageTabsTests.FakeCommands();
        commands.Files.Add(Listed("a.3mf"));
        var order = new List<string>();
        commands.Details = (names, _) =>
        {
            order.Add("details");
            return Task.FromResult<IReadOnlyList<PrintMetadata>>([]);
        };
        commands.Thumbnails = (_, _) =>
        {
            order.Add("thumbnail");
            return Task.FromResult<PrintThumbnail?>(null);
        };
        var picker = Picker(commands);

        await picker.LoadDataCommand.ExecuteAsync(null);
        await picker.ThumbnailsLoading;

        Assert.Equal(["details", "thumbnail"], order);
    }

    [Fact]
    public async Task Files_already_described_by_the_listing_are_not_asked_for_again()
    {
        var commands = new PrinterPageTabsTests.FakeCommands();
        commands.Files.AddRange([Listed("new.3mf"), new PrintMetadata("read.3mf", 10, "PLA", 0.4, 60, Today.AddDays(-1))]);
        var picker = Picker(commands);

        await picker.LoadDataCommand.ExecuteAsync(null);
        await picker.ThumbnailsLoading;

        Assert.Equal(["new.3mf"], Assert.Single(commands.DetailRequests));
    }

    [Fact]
    public async Task Files_whose_details_could_not_be_read_stop_saying_they_are_coming()
    {
        var commands = new PrinterPageTabsTests.FakeCommands { Details = (_, _) => throw new InvalidOperationException("printer went away") };
        commands.Files.Add(Listed("a.3mf"));
        var picker = Picker(commands);

        await picker.LoadDataCommand.ExecuteAsync(null);
        await picker.ThumbnailsLoading;

        var item = Assert.Single(picker.Files);
        Assert.False(item.Metadata.DetailsPending);
        Assert.Equal(string.Empty, item.DetailsText);
        Assert.Null(picker.ErrorMessage); // the list itself loaded; only its details are missing
    }
}
