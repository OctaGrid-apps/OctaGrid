using System.Collections.Concurrent;
using FluentFTP;

namespace OctaGrid.Connectors.BambuLab;

/// <summary>Creates the pool of FTPS connections to one printer.</summary>
internal static class BambuFtpPool
{
    public static BambuFtpPool<AsyncFtpClient> Create(Func<AsyncFtpClient> factory) =>
        new(
            async cancellationToken =>
            {
                var client = factory();
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
            },
            client => client.IsConnected,
            async client =>
            {
                try
                {
                    if (client.IsConnected)
                    {
                        await client.Disconnect(CancellationToken.None);
                    }
                }
                finally
                {
                    client.Dispose();
                }
            });
}

/// <summary>
/// Reuses FTPS connections to one printer. Connecting costs a TLS handshake that is slow on the printer, and a file list
/// followed by its thumbnails used to pay it for every request. At most <see cref="MaxConnections"/> are open at once.
/// </summary>
/// <typeparam name="TClient">The connection; an FTPS client, or a fake in tests.</typeparam>
internal sealed class BambuFtpPool<TClient> : IAsyncDisposable
    where TClient : class
{
    // The printer's vsftpd drops idle sessions after a few minutes; one idle for longer than this is not trusted.
    private static readonly TimeSpan MaxIdle = TimeSpan.FromSeconds(60);

    private readonly Func<TClient, Task> _close;
    private readonly Func<CancellationToken, Task<TClient>> _connect;
    private readonly ConcurrentStack<(TClient Client, long ReturnedAt)> _idle = new();
    private readonly Func<TClient, bool> _isConnected;
    private readonly SemaphoreSlim _slots;
    private readonly TimeProvider _time;
    private volatile bool _disposed;

    public BambuFtpPool(
        Func<CancellationToken, Task<TClient>> connect,
        Func<TClient, bool> isConnected,
        Func<TClient, Task> close,
        int maxConnections = 3,
        TimeProvider? time = null)
    {
        _connect = connect;
        _isConnected = isConnected;
        _close = close;
        MaxConnections = maxConnections;
        _slots = new SemaphoreSlim(maxConnections, maxConnections);
        _time = time ?? TimeProvider.System;
    }

    public int MaxConnections { get; }

    /// <summary>
    /// Runs <paramref name="operation"/> on a connected client, waiting for one to be free. A kept connection the printer
    /// has dropped still looks connected, so an operation that fails on one is run once more on a new connection; an
    /// operation must therefore be safe to repeat (every one here only reads).
    /// </summary>
    public async Task<T> RunAsync<T>(Func<TClient, Task<T>> operation, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _slots.WaitAsync(cancellationToken);
        try
        {
            if (await TakeIdle() is { } kept)
            {
                try
                {
                    var result = await operation(kept);
                    await ReturnAsync(kept);
                    return result;
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    await CloseAsync(kept);
                }
            }

            var client = await _connect(cancellationToken);
            try
            {
                var result = await operation(client);
                await ReturnAsync(client);
                return result;
            }
            catch
            {
                // A connection whose operation failed may be in any state, so it is closed rather than reused.
                await CloseAsync(client);
                throw;
            }
        }
        finally
        {
            _slots.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        await CloseIdleAsync();
    }

    private async Task ReturnAsync(TClient client)
    {
        _idle.Push((client, _time.GetTimestamp()));

        // DisposeAsync may have closed the idle connections between the check in RunAsync and this push.
        if (_disposed)
        {
            await CloseIdleAsync();
        }
    }

    private async Task CloseIdleAsync()
    {
        while (_idle.TryPop(out var idle))
        {
            await CloseAsync(idle.Client);
        }
    }

    private async Task<TClient?> TakeIdle()
    {
        while (_idle.TryPop(out var idle))
        {
            if (_isConnected(idle.Client) && _time.GetElapsedTime(idle.ReturnedAt) < MaxIdle)
            {
                return idle.Client;
            }

            await CloseAsync(idle.Client);
        }

        return null;
    }

    private async Task CloseAsync(TClient client)
    {
        try
        {
            await _close(client);
        }
        catch (Exception)
        {
            // Closing is best effort: the printer may already have dropped the session.
        }
    }
}
