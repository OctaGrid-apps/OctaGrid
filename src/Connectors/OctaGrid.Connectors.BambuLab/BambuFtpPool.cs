using System.Collections.Concurrent;
using FluentFTP;
using FluentFTP.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OctaGrid.Connectors.BambuLab;

/// <summary>Creates the pool of FTPS connections to one printer.</summary>
internal static class BambuFtpPool
{
    public static BambuFtpPool<AsyncFtpClient> Create(Func<AsyncFtpClient> factory, ILogger? logger = null) =>
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
            },
            // The server answered with an error code, so the connection is still alive.
            isConnectionLost: ex => ex is not FtpCommandException,
            logger: logger);
}

/// <summary>
/// Reuses FTPS connections to one printer, because the TLS handshake is slow on the printer.
/// At most <see cref="MaxConnections"/> are open at once.
/// </summary>
/// <typeparam name="TClient">The connection; an FTPS client, or a fake in tests.</typeparam>
internal sealed class BambuFtpPool<TClient> : IAsyncDisposable
    where TClient : class
{
    // The printer's vsftpd drops idle sessions after a few minutes.
    private static readonly TimeSpan MaxIdle = TimeSpan.FromSeconds(60);

    private readonly Func<TClient, Task> _close;
    private readonly Func<CancellationToken, Task<TClient>> _connect;
    private readonly ConcurrentStack<(TClient Client, long ReturnedAt)> _idle = new();
    private readonly Func<TClient, bool> _isConnected;
    private readonly Func<Exception, bool> _isConnectionLost;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _slots;
    private readonly TimeProvider _time;
    private volatile bool _disposed;

    public BambuFtpPool(
        Func<CancellationToken, Task<TClient>> connect,
        Func<TClient, bool> isConnected,
        Func<TClient, Task> close,
        Func<Exception, bool>? isConnectionLost = null,
        int maxConnections = 3,
        TimeProvider? time = null,
        ILogger? logger = null)
    {
        _connect = connect;
        _isConnected = isConnected;
        _close = close;
        _isConnectionLost = isConnectionLost ?? (_ => true);
        MaxConnections = maxConnections;
        _slots = new SemaphoreSlim(maxConnections, maxConnections);
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
    }

    public int MaxConnections { get; }

    /// <summary>
    /// Runs <paramref name="operation"/> on a connected client, waiting for one to be free.
    /// A kept connection the printer dropped still reports itself connected, so an operation that loses it
    /// runs once more on a new connection. Operations must be safe to repeat.
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
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested && _isConnectionLost(ex))
                {
                    _logger.LogDebug(ex, "Kept FTPS connection failed; retrying on a new connection");
                    await CloseAsync(kept);
                }
                catch
                {
                    await CloseAsync(kept);
                    throw;
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
                // The state of a connection after a failed operation is unknown.
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

        // DisposeAsync may have run between the check in RunAsync and the push.
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
        catch (Exception ex)
        {
            // Best effort: the printer may already have dropped the session.
            _logger.LogDebug(ex, "Closing an FTPS connection failed");
        }
    }
}
