using OctaGrid.Extensions.Abstractions;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Protocol.Agents;
using OctaGrid.Protocol.Extensions;
using OctaGrid.Protocol.Printers;
using StreamJsonRpc;

namespace OctaGrid.Connectors.Moonraker;

internal sealed class MoonrakerClient : IPrinterSession, IPrintControl, IFileStorage, IThumbnails
{
    private const string DefaultGCodeRoot = "gcodes";
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(10);

    private static readonly object SubscribeParams = new
    {
        objects = new
        {
            webhooks = new[] { "state", "state_message" },
            extruder = new[] { "temperature", "target" },
            heater_bed = new[] { "temperature", "target" },
            print_stats = new[] { "filename", "state", "total_duration", "print_duration", "info" },
            display_status = new[] { "progress" }
        }
    };

    private readonly MoonrakerFileClient _fileClient;
    private readonly ILogger<MoonrakerClient> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly PrinterProfile _profile;
    private readonly string _url;

    // Single-flight: while one query is pending, further triggers join it instead of queueing more
    // requests behind it. Against a frozen Moonraker every extra request would just wait for the
    // same timeout, and they'd pile up with each view switch.
    private readonly object _queryGate = new();

    private readonly TimeSpan _reconnectDelay = TimeSpan.FromSeconds(5);
    private JsonRpc? _jsonRpc;
    private MoonrakerListener? _listener;

    private Task? _queryInFlight;

    public MoonrakerClient(PrinterProfile profile, ILoggerFactory? loggerFactory = null)
    {
        _profile = profile;
        _url = profile.Get("url") ?? string.Empty;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<MoonrakerClient>();
        _fileClient = new MoonrakerFileClient(profile.Get("url") ?? string.Empty, _loggerFactory.CreateLogger<MoonrakerFileClient>());
    }

    private Guid DeviceId => _profile.PrinterId;

    public async Task DeleteFileAsync(string fileName, CancellationToken cancellationToken)
    {
        var rpc = _jsonRpc
            ?? throw new InvalidOperationException("Not connected to the printer.");

        _logger.LogInformation("Deleting file '{FileName}' on Moonraker device {DeviceId}", fileName, DeviceId);
        await rpc.InvokeWithParameterObjectAsync<JsonElement>("server.files.delete_file", new { path = $"{DefaultGCodeRoot}/{fileName}" }, cancellationToken: cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    public async Task<IReadOnlyList<PrintMetadata>> ListFilesAsync(CancellationToken cancellationToken)
    {
        var rpc = _jsonRpc
            ?? throw new InvalidOperationException("Not connected to the printer.");

        _logger.LogDebug("Listing files on Moonraker device {DeviceId}", DeviceId);
        var files = await rpc.InvokeWithParameterObjectAsync<JsonElement>("server.files.list", cancellationToken: cancellationToken);

        var filePaths = files.EnumerateArray()
            .Select(f => f.GetProperty("path").GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        var metadata = new List<PrintMetadata>(filePaths.Count);

        foreach (var filePath in filePaths)
        {
            var fileMetadata = await MoonrakerFileClient.GetFileMetadataAsync(rpc, filePath!, cancellationToken, _logger).ConfigureAwait(false);
            metadata.Add(new PrintMetadata(fileMetadata.FileName, fileMetadata.FileSize, fileMetadata.MaterialType, fileMetadata.NozzleDiameter, fileMetadata.EstimatedTimeSeconds));
        }

        _logger.LogDebug("Retrieved metadata for {FileCount} file(s) on Moonraker device {DeviceId}", metadata.Count, DeviceId);
        return metadata;
    }

    // Unlike RequestStateAsync/QueryStateAsync above, this result goes straight back to the
    // caller rather than arriving later via telemetry, so — unlike those — the caller's
    // cancellationToken is honoured (MoonrakerFileClient still bounds the RPC with its own timeout).
    public async Task<PrintThumbnail?> GetThumbnailAsync(string fileName, CancellationToken cancellationToken)
    {
        // Captured once, same reasoning as QueryStateAsync: the session can end (and null these)
        // while the call is in flight.
        var rpc = _jsonRpc;
        var listener = _listener;
        if (rpc is null)
        {
            return null;
        }

        // ToDo: Cache the result for the current file name
        var result = await MoonrakerFileClient.GetFileMetadataAsync(rpc, fileName, cancellationToken, _logger).ConfigureAwait(false);

        listener?.OnFileMetadata(result.EstimatedTimeSeconds);

        if (result.ThumbnailFilePath is null)
        {
            return null;
        }

        var thumbnail = await _fileClient.DownloadBestThumbnailAsync(fileName, result.ThumbnailFilePath, cancellationToken).ConfigureAwait(false);
        return thumbnail;
    }

    public async Task PausePrintAsync(CancellationToken cancellationToken)
    {
        var rpc = _jsonRpc
            ?? throw new InvalidOperationException("Not connected to the printer.");

        _logger.LogInformation("Pausing print job on Moonraker device {DeviceId}", DeviceId);
        await rpc.InvokeWithParameterObjectAsync<JsonElement>("printer.print.pause", cancellationToken: cancellationToken);
    }

    public async Task ResumePrintAsync(CancellationToken cancellationToken)
    {
        var rpc = _jsonRpc
            ?? throw new InvalidOperationException("Not connected to the printer.");

        _logger.LogInformation("Resuming print job on Moonraker device {DeviceId}", DeviceId);
        await rpc.InvokeWithParameterObjectAsync<JsonElement>("printer.print.resume", cancellationToken: cancellationToken);
    }

    public async Task RunAsync(ITelemetrySink telemetry, CancellationToken cancellationToken)
    {
        var uri = BuildUri(_url);
        var delay = _reconnectDelay;

        while (!cancellationToken.IsCancellationRequested)
        {
            var connected = false;
            try
            {
                using var ws = new ClientWebSocket();
                // Ping every 15s AND fail the socket if no pong arrives within the timeout. Without a
                // timeout a frozen peer is never detected (the socket just looks open forever), so
                // pending requests hang and the reconnect loop below never gets a chance to run.
                ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                ws.Options.KeepAliveTimeout = TimeSpan.FromSeconds(10);

                _logger.LogDebug("Connecting to Moonraker device {DeviceId} at {Uri}", DeviceId, uri);
                using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    connectCts.CancelAfter(ConnectTimeout);
                    await ws.ConnectAsync(uri, connectCts.Token).ConfigureAwait(false);
                }

                connected = true;
                _logger.LogInformation("Connected to Moonraker device {DeviceId} at {Uri}", DeviceId, uri);
                await RunSessionAsync(DeviceId, ws, telemetry, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Moonraker connection to {Uri} closed for device {DeviceId}", uri, DeviceId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Moonraker connection to {Uri} failed for device {DeviceId}", uri, DeviceId);
            }

            if (!connected)
            {
                telemetry.Send(new Protocol.Messaging.PrinterTelemetry(DeviceId) { State = Protocol.Messaging.UniversalPrinterState.Offline });
            }

            // Reset backoff after a successful connection, otherwise grow it.
            delay = connected
                ? _reconnectDelay
                : TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxReconnectDelay.Ticks));

            _logger.LogDebug("Reconnecting to Moonraker device {DeviceId} in {DelaySeconds}s", DeviceId, delay.TotalSeconds);
            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public async Task StartPrintAsync(string fileName, CancellationToken cancellationToken)
    {
        var rpc = _jsonRpc
            ?? throw new InvalidOperationException("Not connected to the printer.");

        _logger.LogInformation("Starting print job '{FileName}' on Moonraker device {DeviceId}", fileName, DeviceId);
        await rpc.InvokeWithParameterObjectAsync<JsonElement>("printer.print.start", new { filename = fileName }, cancellationToken: cancellationToken);
    }

    public async Task StopPrintAsync(CancellationToken cancellationToken)
    {
        var rpc = _jsonRpc
            ?? throw new InvalidOperationException("Not connected to the printer.");

        _logger.LogInformation("Stopping print job on Moonraker device {DeviceId}", DeviceId);
        await rpc.InvokeWithParameterObjectAsync<JsonElement>("printer.print.cancel", cancellationToken: cancellationToken);
    }

    // Deliberately independent of RunAsync/_jsonRpc/_listener: this runs before the printer is
    // saved, so there is no session (and may never be one, if the test fails). It opens its own
    // socket and RPC channel, confirms the peer actually answers JSON-RPC (not just that the port
    // is open), and tears both down — never touching the instance fields the live session owns.
    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var uri = BuildUri(_url);

            using var ws = new ClientWebSocket();
            if (_profile.Settings.TryGetValue("apiKey", out var apiKey) && !string.IsNullOrEmpty(apiKey))
            {
                // Moonraker's [authorization] block can require this on the websocket upgrade itself.
                ws.Options.SetRequestHeader("X-Api-Key", apiKey);
            }

            try
            {
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectCts.CancelAfter(ConnectTimeout);
                await ws.ConnectAsync(uri, connectCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("Moonraker connection test timed out connecting to {Uri}", uri);
                return ConnectionTestResult.Fail($"Timed out connecting to {uri}.");
            }
            catch (WebSocketException ex)
            {
                _logger.LogDebug(ex, "Moonraker connection test WebSocket error for {Uri}", uri);
                return ConnectionTestResult.Fail(ex.InnerException?.Message ?? ex.Message);
            }

            // A reachable websocket isn't proof it's actually Moonraker — confirm it answers JSON-RPC.
            var handler = new WebSocketMessageHandler(ws, new SystemTextJsonFormatter());
            var rpc = new JsonRpc(handler);
            rpc.StartListening();

            try
            {
                var call = rpc.InvokeWithParameterObjectAsync<JsonElement>("server.info", argument: null, cancellationToken: CancellationToken.None);
                await call.WaitAsync(QueryTimeout, cancellationToken).ConfigureAwait(false);
                return ConnectionTestResult.Ok();
            }
            catch (TimeoutException)
            {
                _logger.LogDebug("Moonraker connection test RPC server.info timed out for {Uri}", uri);
                return ConnectionTestResult.Fail("Connected, but the server did not answer in time.");
            }
            catch (Exception ex) when (ex is ConnectionLostException or RemoteInvocationException)
            {
                _logger.LogDebug(ex, "Moonraker connection test RPC server.info failed for {Uri}", uri);
                return ConnectionTestResult.Fail($"Connected, but the request failed: {ex.Message}");
            }
            finally
            {
                await CloseQuietlyAsync(ws).ConfigureAwait(false);
                rpc.Dispose();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Unexpected error during Moonraker connection test for '{Endpoint}'", _url);
            // Last-resort net: a connection test must report failure, never crash the caller.
            return ConnectionTestResult.Fail(ex.Message);
        }
    }

    public Task RequestStateAsync(CancellationToken cancellationToken)
    {
        lock (_queryGate)
        {
            if (_queryInFlight is { IsCompleted: false })
            {
                return _queryInFlight;
            }

            return _queryInFlight = QueryStateAsync();
        }
    }

    public Task UploadFileAsync(Stream content, string fileName, CancellationToken cancellationToken)
    {
        return _fileClient.UploadFileAsync(content, fileName, cancellationToken);
    }

    private static Uri BuildUri(string connectionString) => new($"ws://{connectionString}/websocket");

    private static async Task CloseQuietlyAsync(ClientWebSocket ws)
    {
        if (ws.State != WebSocketState.Open)
        {
            return;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Shutting down", cts.Token)
                .ConfigureAwait(false);
        }
        catch
        {
            // Best effort only.
        }
    }

    private async Task QueryStateAsync()
    {
        // Capture once: the session can end (and null these) while the call is in flight.
        var rpc = _jsonRpc;
        var listener = _listener;
        if (rpc is null || listener is null)
        {
            return;
        }

        // The result is delivered through the listener (telemetry), not returned to the caller, so
        // the caller's token deliberately does NOT cancel the query. A UI command being superseded
        // (view switch → Loaded fires again → toolkit cancels the previous run) must not abort a
        // request already on the wire, or the printer state would never arrive. The query is
        // bounded by its own timeout instead.
        // WaitAsync(timeout) rather than a cancellation token: StreamJsonRpc keeps waiting for the
        // server's reply after a cancel, so a token would NOT release us from a frozen peer.
        var call = rpc.InvokeWithParameterObjectAsync<JsonElement>("printer.objects.query", SubscribeParams, CancellationToken.None);

        try
        {
            var initialStatus = await call.WaitAsync(QueryTimeout).ConfigureAwait(false);
            listener.OnStatusUpdate(initialStatus, DateTime.UtcNow.Millisecond);
        }
        catch (TimeoutException)
        {
            // The abandoned call faults when the connection is disposed below; observe it.
            _ = call.ContinueWith(static t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

            _logger.LogWarning("Moonraker state query timed out for device {DeviceId}; tearing down frozen RPC session", DeviceId);

            // No answer within the timeout: the connection is frozen, not merely slow. Tear it down
            // so the session ends and RunAsync reconnects + re-subscribes (which pushes fresh
            // state), rather than leaving a dead session that every later trigger would hit too.
            // Not thrown: callers are UI commands and can't do anything useful with it.
            rpc.Dispose();
        }
        catch (Exception ex) when (ex is ConnectionLostException or ObjectDisposedException)
        {
            _logger.LogDebug(ex, "Moonraker state query aborted because connection dropped for device {DeviceId}", DeviceId);
            // The connection dropped/restarted mid-request. The reconnect re-subscribes and pushes
            // fresh state, so there is nothing to retry here.
        }
    }

    private async Task RunSessionAsync(Guid deviceId, ClientWebSocket ws, ITelemetrySink telemetry, CancellationToken ct)
    {
        _listener = new MoonrakerListener(deviceId, telemetry, _loggerFactory.CreateLogger<MoonrakerListener>());
        var handler = new WebSocketMessageHandler(ws, new SystemTextJsonFormatter());
        var rpc = new JsonRpc(handler, _listener);
        _jsonRpc = rpc;
        rpc.StartListening();

        // Everything after StartListening is inside the try, so a failed or hung subscribe still
        // tears the connection down (and the fields are cleared) instead of leaking a dead session.
        try
        {
            // Bounded: a peer that accepts the socket but never answers must not hang the session.
            var subscribeCall = rpc.InvokeWithParameterObjectAsync<JsonElement>("printer.objects.subscribe", SubscribeParams, CancellationToken.None);
            var initialStatus = await subscribeCall.WaitAsync(QueryTimeout, ct).ConfigureAwait(false);
            _listener.OnStatusUpdate(initialStatus, DateTime.UtcNow.Millisecond);
            _logger.LogDebug("Subscribed to Moonraker objects for device {DeviceId}", deviceId);

            // Completes when the connection drops. No polling needed.
            await rpc.Completion.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await CloseQuietlyAsync(ws).ConfigureAwait(false);
            throw;
        }
        finally
        {
            // Don't leave the fields pointing at a disposed connection: RequestStateAsync
            // would keep invoking on it and get its pending calls cancelled.
            _jsonRpc = null;
            _listener = null;
            rpc.Dispose();
        }
    }
}
