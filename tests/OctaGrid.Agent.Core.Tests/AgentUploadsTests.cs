using OctaGrid.Agent.Core.Link;
using OctaGrid.Extensions.Models.Exceptions;

namespace OctaGrid.Agent.Core.Tests;

public sealed class AgentUploadsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "octagrid-tests", "uploads", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string[] FilesOnDisk() => Directory.Exists(_folder) ? Directory.GetFiles(_folder) : [];

    [Fact]
    public async Task Pieces_sent_in_order_come_back_as_one_file()
    {
        using var uploads = new AgentUploads(_folder);
        var id = uploads.Begin(length: 5);

        await uploads.AppendAsync(id, 0, new byte[] { 1, 2, 3 }, Ct);
        await uploads.AppendAsync(id, 1, new byte[] { 4, 5 }, Ct);
        using var received = await uploads.CompleteAsync(id, extension: null, Ct);

        Assert.Equal(5, received.Length);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, await File.ReadAllBytesAsync(received.Path, Ct));
    }

    [Fact]
    public async Task The_file_is_deleted_when_the_received_upload_is_disposed()
    {
        using var uploads = new AgentUploads(_folder);
        var id = uploads.Begin(null);
        await uploads.AppendAsync(id, 0, new byte[] { 1 }, Ct);

        var received = await uploads.CompleteAsync(id, ".octaext", Ct);
        Assert.EndsWith(".octaext", received.Path);
        Assert.True(File.Exists(received.Path));
        received.Dispose();

        Assert.Empty(FilesOnDisk());
    }

    [Fact]
    public async Task A_piece_out_of_order_is_refused()
    {
        using var uploads = new AgentUploads(_folder);
        var id = uploads.Begin(null);
        await uploads.AppendAsync(id, 0, new byte[] { 1 }, Ct);

        await Assert.ThrowsAsync<ValidationException>(() => uploads.AppendAsync(id, 2, new byte[] { 2 }, Ct));
    }

    [Fact]
    public void An_upload_announced_as_too_large_is_refused_before_anything_is_sent()
    {
        using var uploads = new AgentUploads(_folder, maxBytes: 10);

        Assert.Throws<ValidationException>(() => uploads.Begin(11));
        Assert.Empty(FilesOnDisk());
    }

    [Fact]
    public async Task An_upload_that_grows_over_the_limit_is_refused()
    {
        using var uploads = new AgentUploads(_folder, maxBytes: 3);
        var id = uploads.Begin(null);
        await uploads.AppendAsync(id, 0, new byte[] { 1, 2 }, Ct);

        await Assert.ThrowsAsync<ValidationException>(() => uploads.AppendAsync(id, 1, new byte[] { 3, 4 }, Ct));
    }

    [Fact]
    public async Task A_piece_larger_than_the_chunk_size_is_refused()
    {
        using var uploads = new AgentUploads(_folder);
        var id = uploads.Begin(null);

        await Assert.ThrowsAsync<ValidationException>(
            () => uploads.AppendAsync(id, 0, new byte[AgentUploads.MaxChunkSize + 1], Ct));
    }

    [Fact]
    public async Task An_aborted_upload_leaves_no_file_and_cannot_be_used()
    {
        using var uploads = new AgentUploads(_folder);
        var id = uploads.Begin(null);
        await uploads.AppendAsync(id, 0, new byte[] { 1 }, Ct);

        uploads.Abort(id);
        uploads.Abort(id);

        Assert.Empty(FilesOnDisk());
        await Assert.ThrowsAsync<ValidationException>(() => uploads.CompleteAsync(id, null, Ct));
    }

    [Fact]
    public async Task An_upload_cannot_be_used_twice()
    {
        using var uploads = new AgentUploads(_folder);
        var id = uploads.Begin(null);
        using var first = await uploads.CompleteAsync(id, null, Ct);

        await Assert.ThrowsAsync<ValidationException>(() => uploads.CompleteAsync(id, null, Ct));
    }

    [Fact]
    public async Task An_upload_that_received_nothing_for_a_while_is_discarded_when_the_next_one_starts()
    {
        var time = new ManualTime();
        using var uploads = new AgentUploads(_folder, idleTimeout: TimeSpan.FromMinutes(10), timeProvider: time);
        var abandoned = uploads.Begin(null);
        await uploads.AppendAsync(abandoned, 0, new byte[] { 1 }, Ct);

        time.Now += TimeSpan.FromMinutes(11);
        var fresh = uploads.Begin(null);

        await Assert.ThrowsAsync<ValidationException>(() => uploads.AppendAsync(abandoned, 1, new byte[] { 2 }, Ct));
        Assert.Single(FilesOnDisk());
        uploads.Abort(fresh);
    }

    [Fact]
    public async Task Disposing_discards_what_is_unfinished()
    {
        var uploads = new AgentUploads(_folder);
        var id = uploads.Begin(null);
        await uploads.AppendAsync(id, 0, new byte[] { 1 }, Ct);

        uploads.Dispose();

        Assert.Empty(FilesOnDisk());
    }
}
