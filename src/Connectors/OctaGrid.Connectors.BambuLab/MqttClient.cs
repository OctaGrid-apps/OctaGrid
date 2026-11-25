using System.Diagnostics;
using System.Security.Authentication;
using System.Text.Json;
using FluentFTP;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTnet;
using MQTTnet.Exceptions;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using OctaGrid.Extensions.Abstractions;
using OctaGrid.Extensions.Models;
using StorageUnavailableException = OctaGrid.Extensions.Models.Exceptions.StorageUnavailableException;

namespace OctaGrid.Connectors.BambuLab;

internal sealed class MqttClient : IPrinterSession, IPrintControl, IFileStorage, IThumbnails
{
    private const int DefaultPort = 8883;
    private const string Username = "bblp";

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan InitialStateRetryInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PushAllMinInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);
    private const string NoSdCardMessage =
        "This printer has no SD card. Files in its internal storage can only be started from the printer's screen.";

    private readonly MqttClientFactory _factory = new();
    private readonly BambuFileCache _files = new();
    private readonly BambuFtpPool? _ftp;
    private readonly ILogger<MqttClient> _logger;
    private readonly PrinterProfile _profile;
    private readonly string _url;
    private volatile IMqttClient? _client;
    private long _lastPushAll;
    private volatile string? _serial;

    // -1 until a report says, then 0 (no card) or 1. Read by file requests, written by the MQTT thread.
    private int _sdCard = -1;

    public MqttClient(PrinterProfile profile, ILogger<MqttClient>? logger = null)
    {
        _profile = profile;
        _url = profile.Get("url") ?? string.Empty;
        _logger = logger ?? NullLogger<MqttClient>.Instance;

        // The profile does not change during a session (a change starts a new one), so neither does the access code.
        if (profile.Settings.TryGetValue("apiKey", out var accessCode) && !string.IsNullOrEmpty(accessCode))
        {
            _ftp = new BambuFtpPool(() => BambuFtps.CreateClient(_url, accessCode, _logger));
        }
    }

    private Guid DeviceId => _profile.PrinterId;

    private bool HasNoSdCard => Volatile.Read(ref _sdCard) == 0;

    public Task DeleteFileAsync(string fileName, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public async ValueTask DisposeAsync()
    {
        // Each session owns and disposes its own IMqttClient, and PrinterOrchestrator awaits RunAsync
        // before disposing the device client; only the FTPS connections kept for reuse are left to close.
        if (_ftp is not null)
        {
            await _ftp.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }

    public async Task<IReadOnlyList<PrintMetadata>> ListFilesAsync(CancellationToken cancellationToken)
    {
        if (_ftp is null)
        {
            _logger.LogWarning("Cannot list files on Bambu printer {DeviceId}: LAN access code is not configured", DeviceId);
            return [];
        }

        if (HasNoSdCard)
        {
            throw new StorageUnavailableException(NoSdCardMessage);
        }

        var sw = Stopwatch.StartNew();
        var listing = await _ftp.RunAsync(ftp => ftp.GetListing("/", cancellationToken), cancellationToken);
        var threeMfFiles = listing
            .Where(i => i.Type == FtpObjectType.File &&
                        i.Name.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase) &&
                        i.Size > 0)
            .ToList();
        _files.SetListing(threeMfFiles.Select(i => i.Name).ToList());

        // Only files that are new or changed since they were last read cost a read; the rest come from the cache.
        var toRead = threeMfFiles.Where(i => !_files.TryGet(i.Name, i.Size, ModifiedAt(i), out _)).ToList();
        await Parallel.ForEachAsync(toRead, new ParallelOptions
        {
            MaxDegreeOfParallelism = _ftp.MaxConnections,
            CancellationToken = cancellationToken,
        }, async (item, ct) =>
        {
            try
            {
                await ReadFileAsync(_ftp, item, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One file that cannot be read must not hide the others; it is listed with what the listing says.
                _logger.LogWarning(ex, "Failed to read 3MF file '{FileName}' on Bambu printer {DeviceId}", item.Name, DeviceId);
            }
        });

        var files = threeMfFiles
            .Select(i => _files.TryGet(i.Name, i.Size, ModifiedAt(i), out var entry)
                ? entry.Metadata
                : new PrintMetadata(i.Name, i.Size, null, null, null, ModifiedAt(i)))
            .ToList();

        _logger.LogInformation("Listed {FileCount} 3MF file(s) on Bambu printer {DeviceId}, read {ReadCount}, in {ElapsedMs}ms",
            files.Count, DeviceId, toRead.Count, sw.ElapsedMilliseconds);
        return files;
    }

    /// <summary>
    /// The thumbnail of a file on the card, or of the current print job when <paramref name="fileName"/> is the job's
    /// name as the printer reports it. Files already listed are answered from the cache without touching the printer.
    /// </summary>
    public async Task<PrintThumbnail?> GetThumbnailAsync(string fileName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileName) || _ftp is null || HasNoSdCard)
        {
            return null;
        }

        try
        {
            var name = fileName.TrimStart('/', '\\').Replace('\\', '/');
            if (!name.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase))
            {
                var names = _files.ListedNames ?? await ListThreeMfNamesAsync(_ftp, cancellationToken);
                var resolved = BambuFileCache.ResolveJobFile(name, names);
                if (resolved is null)
                {
                    // Jobs started from internal storage or sent from Bambu Studio are not on the card.
                    _logger.LogDebug("No file on the card of Bambu printer {DeviceId} matches print job '{JobName}'", DeviceId, name);
                    return null;
                }

                name = resolved;
            }

            if (!_files.TryGet(name, out var entry))
            {
                entry = await _ftp.RunAsync(async ftp =>
                {
                    var item = await ftp.GetObjectInfo("/" + name, token: cancellationToken);
                    return item is null ? null : await ReadFileAsync(ftp, item, cancellationToken);
                }, cancellationToken);
            }

            return entry?.Thumbnail is { Length: > 0 } thumbnail ? new PrintThumbnail("image/png", thumbnail) : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Failed to fetch thumbnail for '{FileName}' on Bambu printer {DeviceId}", fileName, DeviceId);
            return null;
        }
    }

    // FluentFTP gives no time zone; the printers' vsftpd lists times in UTC, and the H2 series only the date.
    private static DateTimeOffset? ModifiedAt(FtpListItem item) =>
        item.Modified == DateTime.MinValue ? null : new DateTimeOffset(DateTime.SpecifyKind(item.Modified, DateTimeKind.Utc));

    private async Task<IReadOnlyList<string>> ListThreeMfNamesAsync(BambuFtpPool pool, CancellationToken cancellationToken)
    {
        var listing = await pool.RunAsync(ftp => ftp.GetListing("/", cancellationToken), cancellationToken);
        var names = listing
            .Where(i => i.Type == FtpObjectType.File && i.Name.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase))
            .Select(i => i.Name)
            .ToList();
        _files.SetListing(names);
        return names;
    }

    private Task<BambuFileCache.Entry?> ReadFileAsync(BambuFtpPool pool, FtpListItem item, CancellationToken cancellationToken) =>
        pool.RunAsync(ftp => ReadFileAsync(ftp, item, cancellationToken), cancellationToken);

    private async Task<BambuFileCache.Entry?> ReadFileAsync(AsyncFtpClient ftp, FtpListItem item, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var result = await TailThreeMfFtpReader.ExtractFromTailAsync(ftp, item, extractThumbnail: true, logger: _logger, cancellationToken: cancellationToken);
        _logger.LogDebug("Read 3MF file '{FileName}' ({FileSize} bytes) in {ElapsedMs}ms on Bambu printer {DeviceId}",
            item.Name, item.Size, sw.ElapsedMilliseconds, DeviceId);

        var modifiedAt = ModifiedAt(item);
        var metadata = result?.Metadata is { } read
            ? read with { FileName = item.Name, FileSize = item.Size, ModifiedAt = modifiedAt }
            : new PrintMetadata(item.Name, item.Size, null, null, null, modifiedAt);

        // A file that is not a readable 3MF is remembered too, so it is not read again until it changes.
        var entry = new BambuFileCache.Entry(item.Size, modifiedAt, metadata, result?.Thumbnail);
        _files.Store(item.Name, entry);
        return entry;
    }

    public Task PausePrintAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task ResumePrintAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public async Task RunAsync(ITelemetrySink telemetry, CancellationToken cancellationToken)
    {
        if (!TryGetEndpoint(out var host, out var port))
        {
            _logger.LogWarning("Bambu printer {DeviceId} has invalid endpoint '{Endpoint}'", DeviceId, _url);
            ReportOffline(telemetry, $"'{_url}' is not a valid printer address.");
            await WaitUntilCancelledAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        _profile.Settings.TryGetValue("apiKey", out var accessCode);
        if (string.IsNullOrEmpty(accessCode))
        {
            _logger.LogWarning("Bambu printer {DeviceId} ({Host}:{Port}) is missing LAN access code", DeviceId, host, port);
            ReportOffline(telemetry, "The printer's LAN access code is not set.");
            await WaitUntilCancelledAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var delay = ReconnectDelay;
        string? lastOffline = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            var established = false;
            string failure;
            try
            {
                _logger.LogDebug("Connecting to Bambu MQTT broker on device {DeviceId} at {Host}:{Port}", DeviceId, host, port);
                established = await RunSessionAsync(host, port, accessCode, telemetry, cancellationToken)
                    .ConfigureAwait(false);
                failure = "The connection to the printer was lost.";
                _logger.LogInformation("Bambu MQTT session ended for device {DeviceId} at {Host}:{Port}", DeviceId, host, port);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                failure = Describe(ex, host, port);
                _logger.LogWarning(ex, "Bambu MQTT connection to {Host}:{Port} failed for device {DeviceId}: {FailureReason}",
                    host, port, DeviceId, failure);
            }

            // A dropped session is a new outage and is always reported; repeated failed attempts of
            // the same outage are not, or the UI would be re-notified on every backoff tick.
            if (established || failure != lastOffline)
            {
                ReportOffline(telemetry, failure);
            }

            lastOffline = failure;

            // Reset the backoff after a session that actually worked, otherwise grow it.
            delay = established
                ? ReconnectDelay
                : TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxReconnectDelay.Ticks));

            _logger.LogDebug("Reconnecting to Bambu printer {DeviceId} in {DelaySeconds}s", DeviceId, delay.TotalSeconds);
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

    public Task StartPrintAsync(string fileName, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task StopPrintAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task UploadFileAsync(Stream content, string fileName, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    private static string Describe(Exception ex, string host, int port) => ex switch
    {
        BrokerRefusedException
        {
            ResultCode: MqttClientConnectResultCode.BadUserNameOrPassword or MqttClientConnectResultCode.NotAuthorized
        } => "Authentication failed — check the printer's LAN access code.",
        BrokerRefusedException refused => $"The printer refused the connection ({refused.ResultCode}).",
        OperationCanceledException or MqttCommunicationTimedOutException => $"Timed out connecting to {host}:{port}.",
        AuthenticationException => "The TLS handshake with the printer failed.",
        _ => (ex.InnerException ?? ex).Message
    };

    private static async Task DisconnectQuietlyAsync(IMqttClient client)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build(), cts.Token).ConfigureAwait(false);
        }
        catch
        {
            // Best effort only.
        }
    }

    private static bool TryGetSerialFromTopic(string topic, out string serial)
    {
        var parts = topic.Split('/');
        if (parts is ["device", { Length: > 0 } id, "report"])
        {
            serial = id;
            return true;
        }

        serial = string.Empty;
        return false;
    }

    private static async Task WaitUntilCancelledAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnMessage(
            MqttApplicationMessageReceivedEventArgs e,
            IMqttClient client,
            BambuReportMapper mapper,
            ITelemetrySink telemetry,
            TaskCompletionSource firstReport,
            CancellationToken ct)
    {
        // Serial not configured: the first report's topic (device/<serial>/report) tells us. The
        // pushall couldn't be addressed before this, so it goes out now.
        if (_serial is null && TryGetSerialFromTopic(e.ApplicationMessage.Topic, out var learned))
        {
            _serial = learned;
            _logger.LogInformation("Learned serial number '{SerialNumber}' for Bambu printer {DeviceId}", learned, DeviceId);
            _ = RequestFullStateAsync(client, ct);
        }

        using var document = JsonDocument.Parse(e.ApplicationMessage.Payload);
        if (!document.RootElement.TryGetProperty("print", out var print) || print.ValueKind != JsonValueKind.Object)
        {
            return; // "info", "system", ... — not state this client maps yet.
        }

        var changed = mapper.Apply(print);
        if (mapper.HasSdCard is { } hasSdCard)
        {
            Volatile.Write(ref _sdCard, hasSdCard ? 1 : 0);
        }

        firstReport.TrySetResult();

        if (changed)
        {
            telemetry.Send(mapper.Snapshot());
        }
    }

    // The reason goes out as the one condition, which the card shows. The next report carries no
    // conditions, so it clears itself as soon as the printer is back.
    private void ReportOffline(ITelemetrySink telemetry, string reason) =>
        telemetry.Send(new PrinterTelemetry(DeviceId)
        {
            State = UniversalPrinterState.Offline,
            Conditions = [new PrinterCondition("connection.failed", PrinterSeverity.Error, reason)]
        });

    /// <summary>
    /// Asks the printer to publish its complete state. Never throws: a failure here means the
    /// connection is going away, which surfaces through the disconnect handling, not as an error.
    /// </summary>
    private async Task RequestFullStateAsync(IMqttClient client, CancellationToken cancellationToken)
    {
        if (_serial is not { } serial || !client.IsConnected)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var last = Interlocked.Read(ref _lastPushAll);
        if (last != 0 && Stopwatch.GetElapsedTime(last, now) < PushAllMinInterval)
        {
            return;
        }

        Interlocked.Exchange(ref _lastPushAll, now);

        try
        {
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(BambuTopics.Request(serial))
                .WithPayload(Payload.PushAll())
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
                .Build();

            await client.PublishAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is MqttCommunicationException or ObjectDisposedException or OperationCanceledException)
        {
            _logger.LogDebug(ex, "Failed to publish pushall state request to Bambu printer {DeviceId}", DeviceId);
        }
    }

    /// <summary>
    /// One connection's lifetime. Returns true if the session was established and then ended (the
    /// connection dropped); throws if it never got established. A fresh IMqttClient per session
    /// avoids carrying half-dead state from a failed one into the next attempt.
    /// </summary>
    private async Task<bool> RunSessionAsync(string host, int port, string accessCode, ITelemetrySink telemetry, CancellationToken ct)
    {
        using var client = _factory.CreateMqttClient();
        var mapper = new BambuReportMapper(DeviceId);
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstReport = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _serial = _profile.Settings.GetValueOrDefault("serialNumber") is { Length: > 0 } configured ? configured : null;
        Interlocked.Exchange(ref _lastPushAll, 0);

        // Handlers must never throw into MQTTnet's receive loop.
        client.ApplicationMessageReceivedAsync += e =>
        {
            try
            {
                OnMessage(e, client, mapper, telemetry, firstReport, ct);
            }
            catch (JsonException ex)
            {
                // A malformed report isn't worth dropping the connection over.
                _logger.LogDebug(ex, "Ignored malformed JSON report from Bambu printer {DeviceId}", DeviceId);
            }

            return Task.CompletedTask;
        };
        client.DisconnectedAsync += _ =>
        {
            disconnected.TrySetResult();
            return Task.CompletedTask;
        };

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(host, port)
            .WithCredentials(Username, accessCode)
            // Random per session: two OctaGrid instances watching one printer must not share an id,
            // because the broker would then kick each of them off in turn, forever.
            .WithClientId($"octagrid-{Guid.NewGuid():N}"[..21])
            .WithProtocolVersion(MqttProtocolVersion.V311)
            .WithCleanSession()
            .WithKeepAlivePeriod(KeepAlive)
            .WithTimeout(ConnectTimeout)
            .WithTlsOptions(tls => tls
                .UseTls()
                // The printer presents a self-signed certificate (Bambu's own CA), so the chain can't
                // be validated against the system store. Accepted because this only ever talks to a
                // device on the user's LAN, but it does mean the link isn't protected against MITM.
                .WithCertificateValidationHandler(_ => true))
            .Build();

        using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            connectCts.CancelAfter(ConnectTimeout);
            var connected = await client.ConnectAsync(options, connectCts.Token).ConfigureAwait(false);

            // MQTTnet's own exception doesn't expose the broker's reason, so a refused login is
            // checked here, where it can be told apart from a network failure.
            if (connected.ResultCode != MqttClientConnectResultCode.Success)
            {
                throw new BrokerRefusedException(connected.ResultCode);
            }
        }

        _client = client;
        try
        {
            // Exact topic once the serial is known; otherwise the wildcard, which on the printer's
            // own LAN broker only ever matches this one device and lets us learn the serial.
            var reportTopic = _serial is { } serial ? BambuTopics.Report(serial) : BambuTopics.Report("+");
            var subscribeOptions = _factory.CreateSubscribeOptionsBuilder()
                .WithTopicFilter(reportTopic, MqttQualityOfServiceLevel.AtMostOnce)
                .Build();

            var subscribed = await client.SubscribeAsync(subscribeOptions, ct).ConfigureAwait(false);
            if (subscribed.Items.Any(i => (int)i.ResultCode >= 0x80))
            {
                throw new InvalidOperationException($"The printer refused the subscription to '{reportTopic}'.");
            }

            _logger.LogInformation("Connected and subscribed to Bambu printer {DeviceId} at {Host}:{Port} (topic: {Topic})",
                DeviceId, host, port, reportTopic);

            await RequestFullStateAsync(client, ct).ConfigureAwait(false);

            // Wait for the session to end. Until the first report arrives keep asking for the full
            // state, so a single lost request can't leave the card empty. Dead-peer detection is the
            // MQTT keep-alive's job: it raises DisconnectedAsync, which completes `disconnected`.
            while (true)
            {
                var wait = firstReport.Task.IsCompleted ? Timeout.InfiniteTimeSpan : InitialStateRetryInterval;
                var finished = await Task.WhenAny(disconnected.Task, Task.Delay(wait, ct)).ConfigureAwait(false);

                if (finished == disconnected.Task)
                {
                    return true;
                }

                if (ct.IsCancellationRequested)
                {
                    await DisconnectQuietlyAsync(client).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                }

                if (!firstReport.Task.IsCompleted)
                {
                    await RequestFullStateAsync(client, ct).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _client = null;
        }
    }

    private bool TryGetEndpoint(out string host, out int port)
    {
        host = string.Empty;
        port = DefaultPort;

        // Parsed as a URI authority so "192.168.1.50", "192.168.1.50:8883" and "[fe80::1]" all work.
        if (!Uri.TryCreate($"mqtts://{_url}", UriKind.Absolute, out var uri) || uri.Host.Length == 0)
        {
            return false;
        }

        host = uri.Host;
        port = uri.Port == -1 ? DefaultPort : uri.Port;
        return true;
    }

    private sealed class BrokerRefusedException(MqttClientConnectResultCode resultCode) : Exception
    {
        public MqttClientConnectResultCode ResultCode { get; } = resultCode;
    }
}
