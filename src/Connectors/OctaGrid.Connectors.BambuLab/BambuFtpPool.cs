using System.Collections.Concurrent;
using FluentFTP;

namespace OctaGrid.Connectors.BambuLab;

/// <summary>
/// Reuses FTPS connections to one printer. Connecting costs a TLS handshake that is slow on the printer, and a file list
/// followed by its thumbnails used to pay it for every request. At most <see cref="MaxConnections"/> are open at once.
/// </summary>
internal sealed class BambuFtpPool : IAsyncDisposable
{
    // The printer's vsftpd drops idle sessions after a few minutes; one idle for longer than this is not trusted.
    private static readonly TimeSpan MaxIdle = TimeSpan.FromSeconds(60);

    private readonly Func<AsyncFtpClient> _factory;
    private readonly ConcurrentStack<(AsyncFtpClient Client, long ReturnedAt)> _idle = new();
    private readonly SemaphoreSlim _slots;
    private readonly TimeProvider _time;
    private volatile bool _disposed;

    public BambuFtpPool(Func<AsyncFtpClient> factory, int maxConnections = 3, TimeProvider? time = null)
    {
        _factory = factory;
        MaxConnections = maxConnections;
        _slots = new SemaphoreSlim(maxConnections, maxConnections);
        _time = time ?? TimeProvider.System;
    }

    public int MaxConnections { get; }

    /// <summary>Runs <paramref name="operation"/> on a connected client, waiting for one to be free.</summary>
    public async Task<T> RunAsync<T>(Func<AsyncFtpClient, Task<T>> operation, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _slots.WaitAsync(cancellationToken);
        AsyncFtpClient? client = null;
        var healthy = false;
        try
        {
            client = await TakeIdle() ?? await ConnectAsync(cancellationToken);
            var result = await operation(client);
            healthy = true;
            return result;
        }
        finally
        {
            // A connection whose operation failed may be in any state, so it is closed rather than reused.
            if (client is not null)
            {
                if (healthy && !_disposed)
                {
                    _idle.Push((client, _time.GetTimestamp()));
                }
                else
                {
                    await CloseAsync(client);
                }
            }

            _slots.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        while (_idle.TryPop(out var idle))
        {
            await CloseAsync(idle.Client);
        }
    }

    private async Task<AsyncFtpClient?> TakeIdle()
    {
        while (_idle.TryPop(out var idle))
        {
            if (idle.Client.IsConnected && _time.GetElapsedTime(idle.ReturnedAt) < MaxIdle)
            {
                return idle.Client;
            }

            await CloseAsync(idle.Client);
        }

        return null;
    }

    private async Task<AsyncFtpClient> ConnectAsync(CancellationToken cancellationToken)
    {
        var client = _factory();
        try
        {
            await client.Connect(cancellationToken);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task CloseAsync(AsyncFtpClient client)
    {
        try
        {
            if (client.IsConnected)
            {
                await client.Disconnect(CancellationToken.None);
            }
        }
        catch (Exception)
        {
            // Closing is best effort: the printer may already have dropped the session.
        }
        finally
        {
            client.Dispose();
        }
    }
}
