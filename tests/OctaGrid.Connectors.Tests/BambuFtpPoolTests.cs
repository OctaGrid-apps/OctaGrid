using OctaGrid.Connectors.BambuLab;

namespace OctaGrid.Connectors.Tests;

public class BambuFtpPoolTests
{
    private readonly List<FakeConnection> _opened = [];

    private BambuFtpPool<FakeConnection> Pool() =>
        new(
            _ =>
            {
                var connection = new FakeConnection(_opened.Count + 1);
                _opened.Add(connection);
                return Task.FromResult(connection);
            },
            c => !c.Closed,
            c =>
            {
                c.Closed = true;
                return Task.CompletedTask;
            },
            isConnectionLost: ex => ex is IOException);

    [Fact]
    public async Task A_connection_is_kept_and_used_again()
    {
        await using var pool = Pool();

        var first = await pool.RunAsync(c => Task.FromResult(c.Number), TestContext.Current.CancellationToken);
        var second = await pool.RunAsync(c => Task.FromResult(c.Number), TestContext.Current.CancellationToken);

        Assert.Equal(1, first);
        Assert.Equal(1, second);
        Assert.Single(_opened);
    }

    [Fact]
    public async Task An_operation_that_fails_on_a_kept_connection_runs_again_on_a_new_one()
    {
        await using var pool = Pool();
        await pool.RunAsync(c => Task.FromResult(c.Number), TestContext.Current.CancellationToken);

        // The printer dropped the kept session, but the client still says it is connected.
        var result = await pool.RunAsync(
            c => c.Number == 1 ? throw new IOException("Connection reset") : Task.FromResult(c.Number),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, result);
        Assert.True(_opened[0].Closed);
    }

    [Fact]
    public async Task An_error_the_printer_answered_on_a_kept_connection_is_not_retried()
    {
        await using var pool = Pool();
        await pool.RunAsync(c => Task.FromResult(c.Number), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pool.RunAsync<int>(_ => throw new InvalidOperationException("550 File not found"),
                TestContext.Current.CancellationToken));

        Assert.Single(_opened);
    }

    [Fact]
    public async Task An_operation_that_fails_on_a_new_connection_fails_and_closes_it()
    {
        await using var pool = Pool();

        await Assert.ThrowsAsync<IOException>(() =>
            pool.RunAsync<int>(_ => throw new IOException("Connection reset"), TestContext.Current.CancellationToken));

        Assert.True(Assert.Single(_opened).Closed);
    }

    [Fact]
    public async Task A_connection_given_back_after_the_pool_closed_is_closed()
    {
        var pool = Pool();
        var release = new TaskCompletionSource();
        var running = pool.RunAsync(async c =>
        {
            await release.Task;
            return c.Number;
        }, TestContext.Current.CancellationToken);

        await pool.DisposeAsync();
        release.SetResult();
        await running;

        Assert.True(Assert.Single(_opened).Closed);
    }

    internal sealed class FakeConnection
    {
        public FakeConnection(int number)
        {
            Number = number;
        }

        public int Number { get; }

        public bool Closed { get; set; }
    }
}
