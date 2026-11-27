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
using AppException = OctaGrid.Extensions.Models.Exceptions.AppException;
using NotFoundException =OctaGrid.Extensions.Models.Exceptions.NotFoundException;
using StorageUnavailableException = OctaGrid.Extensions.Models.Exceptions.StorageUnavailableException;
using ValidationException = OctaGrid.Extensions.Models.Exceptions.ValidationException;

namespace OctaGrid.Connectors.BambuLab;

internal sealed class MqttClient : IPrinterSession, IPrintControl, IFileStorage, IFileDetails, IThumbnails
{
    private const int DefaultPort = 8883;
    private const string Username = "bblp";

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan InitialStateRetryInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PushAllMinInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(10);
    private const string NoSdCardMessage =
        "This printer has no SD card. Files in its internal storage can only be started from the printer's screen.";
    private const string NotConnectedMessage = "The printer is not connected.";

    private readonly MqttClientFactory _factory = new();
    private readonly BambuFileCache _files = new();
    private readonly BambuCommandReplies _replies = new();
    private readonly BambuFtpPool<AsyncFtpClient>? _ftp;
    private readonly ILogger<MqttClient> _logger;
    private readonly PrinterProfile _profile;
    private readonly string _url;
    private volatile IMqttClient? _client;
    private long _lastPushAll;
    private volatile string? _serial;

    // The files of the last listing by name, replaced as a whole on each listing.
    private volatile IReadOnlyDictionary<string, FtpListItem> _listed = new Dictionary<string, FtpListItem>();

    // The spools of the current session's reports. Written by the MQTT thread.
    private volatile BambuLoadedFilaments _loaded = BambuLoadedFilaments.None;

    // -1 until the first report, then 0 (no card) or 1. Written by the MQTT thread.
    private int _sdCard = -1;

    public MqttClient(PrinterProfile profile, ILogger<MqttClient>? logger = null)
    {
        _profile = profile;
        _url = profile.Get("url") ?? string.Empty;
        _logger = logger ?? NullLogger<MqttClient>.Instance;

        // A profile change starts a new session, so the access code is fixed for this one.
        if (profile.Settings.TryGetValue("apiKey", out var accessCode) && !string.IsNullOrEmpty(accessCode))
        {
            _ftp = BambuFtpPool.Create(() => BambuFtps.CreateClient(_url, accessCode, _logger), _logger);
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
        // before disposing the device client; only the pooled FTPS connections remain.
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

        // One folder listing. Files read before come with their details; the rest are read when the app asks for
        // them (GetFileDetailsAsync), so the list shows at once however many files the card holds.
        var sw = Stopwatch.StartNew();
        var threeMfFiles = await ListThreeMfFilesAsync(_ftp, cancellationToken);
        var files = threeMfFiles
            .Select(i => _files.TryGet(i.Name, i.Size, ModifiedAt(i), out var entry)
                ? entry.Metadata
                : new PrintMetadata(i.Name, i.Size, null, null, null, ModifiedAt(i), DetailsPending: true))
            .ToList();

        _logger.LogInformation("Listed {FileCount} 3MF file(s) on Bambu printer {DeviceId}, {PendingCount} without details, in {ElapsedMs}ms",
            files.Count, DeviceId, files.Count(f => f.DetailsPending), sw.ElapsedMilliseconds);
        return files;
    }

    public async Task<IReadOnlyList<PrintMetadata>> GetFileDetailsAsync(IReadOnlyList<string> fileNames, CancellationToken cancellationToken)
    {
        if (_ftp is null || HasNoSdCard)
        {
            return [];
        }

        // The names come from the last listing, which has each file's size and time; a name it lacks is gone.
        var listed = _listed;
        var items = fileNames
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => listed.GetValueOrDefault(name))
            .OfType<FtpListItem>()
            .ToList();

        var sw = Stopwatch.StartNew();
        var read = 0;
        var details = new PrintMetadata[items.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, items.Count), new ParallelOptions
        {
            MaxDegreeOfParallelism = _ftp.MaxConnections,
            CancellationToken = cancellationToken,
        }, async (index, ct) =>
        {
            var item = items[index];
            if (_files.TryGet(item.Name, item.Size, ModifiedAt(item), out var cached))
            {
                details[index] = cached.Metadata;
                return;
            }

            try
            {
                details[index] = (await ReadFileAsync(_ftp, item, ct))?.Metadata ?? ListedOnly(item);
                Interlocked.Increment(ref read);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Described with what the listing says, so the app does not ask for it again.
                _logger.LogWarning(ex, "Failed to read 3MF file '{FileName}' on Bambu printer {DeviceId}", item.Name, DeviceId);
                details[index] = ListedOnly(item);
            }
        });

        _logger.LogDebug("Read {ReadCount} of {FileCount} requested 3MF file(s) on Bambu printer {DeviceId} in {ElapsedMs}ms",
            read, items.Count, DeviceId, sw.ElapsedMilliseconds);
        return details;
    }

    private static PrintMetadata ListedOnly(FtpListItem item) => new(item.Name, item.Size, null, null, null, ModifiedAt(item));

    /// <summary>
    /// The thumbnail of a .3mf file on the card, or of the print job when <paramref name="fileName"/>
    /// is a job name as the printer reports it.
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
            BambuFileCache.Entry? entry;
            if (name.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase))
            {
                // File names come from a listing, which refreshed the entries of changed files.
                if (!_files.TryGet(name, out entry))
                {
                    entry = await _ftp.RunAsync(async ftp =>
                    {
                        var item = await ftp.GetObjectInfo("/" + name, token: cancellationToken);
                        return item is null ? null : await ReadFileAsync(ftp, item, cancellationToken);
                    }, cancellationToken);
                }
            }
            else
            {
                entry = await GetJobFileAsync(_ftp, name, cancellationToken);
            }

            return entry?.Thumbnail is { Length: > 0 } thumbnail ? new PrintThumbnail("image/png", thumbnail) : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Failed to fetch thumbnail for '{FileName}' on Bambu printer {DeviceId}", fileName, DeviceId);
            return null;
        }
    }

    // FluentFTP gives no time zone. The printers' vsftpd lists UTC times; the H2 series lists dates only.
    private static DateTimeOffset? ModifiedAt(FtpListItem item) =>
        item.Modified == DateTime.MinValue ? null : new DateTimeOffset(DateTime.SpecifyKind(item.Modified, DateTimeKind.Utc));

    // Lists the card again: the job's file may have been added or replaced since the last listing.
    private async Task<BambuFileCache.Entry?> GetJobFileAsync(BambuFtpPool<AsyncFtpClient> pool, string jobName, CancellationToken cancellationToken)
    {
        var files = await ListThreeMfFilesAsync(pool, cancellationToken);
        var resolved = BambuFileCache.ResolveJobFile(jobName, files.Select(i => i.Name));
        if (resolved is null)
        {
            // Jobs started from internal storage or sent from Bambu Studio have no file on the card.
            _logger.LogDebug("No file on the card of Bambu printer {DeviceId} matches print job '{JobName}'", DeviceId, jobName);
            return null;
        }

        var item = files.First(i => string.Equals(i.Name, resolved, StringComparison.OrdinalIgnoreCase));
        return _files.TryGet(item.Name, item.Size, ModifiedAt(item), out var entry)
            ? entry
            : await ReadFileAsync(pool, item, cancellationToken);
    }

    // Lists the printable files on the card and drops cache entries of files that are gone.
    private async Task<IReadOnlyList<FtpListItem>> ListThreeMfFilesAsync(BambuFtpPool<AsyncFtpClient> pool, CancellationToken cancellationToken)
    {
        var listing = await pool.RunAsync(ftp => ftp.GetListing("/", cancellationToken), cancellationToken);
        var files = listing
            .Where(i => i.Type == FtpObjectType.File &&
                        i.Name.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase) &&
                        i.Size > 0)
            .ToList();
        _files.SetListing(files.Select(i => i.Name).ToList());
        _listed = files.ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase);
        return files;
    }

    private Task<BambuFileCache.Entry?> ReadFileAsync(BambuFtpPool<AsyncFtpClient> pool, FtpListItem item, CancellationToken cancellationToken) =>
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
            : ListedOnly(item);

        // Unreadable files are cached too, so they are not read again until they change.
        var entry = new BambuFileCache.Entry(item.Size, modifiedAt, metadata, result?.Thumbnail);
        _files.Store(item.Name, entry);
        return entry;
    }

    public Task PausePrintAsync(CancellationToken cancellationToken) =>
        SendAsync(Payload.Pause(), "pause the print", cancellationToken);

    public Task ResumePrintAsync(CancellationToken cancellationToken) =>
        SendAsync(Payload.Resume(), "resume the print", cancellationToken);

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

    /// <summary>
    /// Prints a .3mf from the SD card's root. The file must have one sliced plate, and each of its filaments a loaded
    /// spool of the same type and colour; otherwise nothing is sent and the reason is thrown.
    /// </summary>
    public async Task StartPrintAsync(string fileName, CancellationToken cancellationToken)
    {
        var name = fileName?.Trim() ?? string.Empty;
        if (name.Length == 0 || name.IndexOfAny(['/', '\\']) >= 0 || name.Any(char.IsControl)
            || !name.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("fileName", "Only .3mf files on the printer's SD card can be started.");
        }

        if (_client is not { IsConnected: true } || _ftp is null)
        {
            throw new NotFoundException(NotConnectedMessage);
        }

        if (HasNoSdCard)
        {
            throw new StorageUnavailableException(NoSdCardMessage);
        }

        var loaded = _loaded;
        if (!loaded.Known)
        {
            throw new ValidationException("fileName", "The printer has not reported its spools yet. Try again in a moment.");
        }

        // Read now rather than from a cache: the file may have been replaced since it was listed.
        bool found;
        byte[]? sliceInfo;
        try
        {
            (found, sliceInfo) = await _ftp.RunAsync(async ftp =>
            {
                var item = await ftp.GetObjectInfo("/" + name, token: cancellationToken);
                return item is null
                    ? (false, null)
                    : (true, await TailThreeMfFtpReader.ReadEntryAsync(ftp, item, BambuSliceInfo.EntryName, _logger, cancellationToken));
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not AppException)
        {
            // FTP failures and damaged archives alike: nothing was sent, and the user can try again.
            _logger.LogWarning(ex, "Failed to read '{FileName}' to start it on Bambu printer {DeviceId}", name, DeviceId);
            throw new StorageUnavailableException($"OctaGrid could not read '{name}' from the printer's SD card. Try again in a moment.");
        }

        if (!found)
        {
            throw new NotFoundException($"'{name}' is not on the printer's SD card.");
        }

        var plates = sliceInfo is null ? [] : BambuSliceInfo.ReadPlates(sliceInfo);
        if (plates.Count == 0)
        {
            throw new ValidationException("fileName", "The file has no sliced plate. Slice it in Bambu Studio and export the sliced file.");
        }

        if (plates.Count > 1)
        {
            throw new ValidationException("fileName",
                $"The file has {plates.Count} sliced plates. OctaGrid can start files with one plate for now: export the plate on its own from Bambu Studio.");
        }

        var plate = plates[0];
        if (!BambuFilamentMatcher.TryMatch(plate, loaded, out var filaments, out var problem))
        {
            throw new ValidationException("fileName", problem);
        }

        _logger.LogInformation(
            "Starting plate {Plate} of '{FileName}' on Bambu printer {DeviceId} (AMS: {UseAms}, trays: {AmsMapping})",
            plate.Index, name, DeviceId, filaments.UseAms, string.Join(",", filaments.AmsMapping));
        await SendAsync(Payload.PrintProjectFile(ProjectFileOptions.FromSdCard(name, plate.Index, filaments)), "start the print", cancellationToken);
    }

    public Task StopPrintAsync(CancellationToken cancellationToken) =>
        SendAsync(Payload.Stop(), "stop the print", cancellationToken);

    /// <summary>
    /// Publishes a command and waits for the printer's answer. A refusal is thrown with the printer's reason. No answer
    /// in time is logged and not thrown: not every firmware answers every command, and the next report shows whether
    /// it took effect.
    /// </summary>
    private async Task SendAsync(BambuCommand command, string action, CancellationToken cancellationToken)
    {
        if (_client is not { IsConnected: true } client || _serial is not { } serial)
        {
            throw new NotFoundException(NotConnectedMessage);
        }

        var reply = _replies.Expect(command);
        try
        {
            // The session may have ended since the check above. Its end clears _client before failing the waiting
            // commands, so a command registered too late for that is caught here instead of timing out as sent.
            if (!ReferenceEquals(_client, client))
            {
                throw new NotFoundException(NotConnectedMessage);
            }

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(BambuTopics.Request(serial))
                .WithPayload(command.Json)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
                .Build();
            try
            {
                await client.PublishAsync(message, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is MqttCommunicationException or ObjectDisposedException)
            {
                _logger.LogWarning(ex, "Failed to send the {Command} command to Bambu printer {DeviceId}", command.Name, DeviceId);
                throw new NotFoundException(NotConnectedMessage);
            }

            BambuCommandReply answer;
            try
            {
                answer = await reply.WaitAsync(ReplyTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Bambu printer {DeviceId} did not answer the {Command} command within {TimeoutSeconds}s",
                    DeviceId, command.Name, ReplyTimeout.TotalSeconds);
                return;
            }

            if (!answer.Succeeded)
            {
                _logger.LogWarning("Bambu printer {DeviceId} refused the {Command} command: {Result} ({Reason})",
                    DeviceId, command.Name, answer.Result, answer.Reason);
                throw new ValidationException("printer", answer.RefusalMessage(action));
            }

            _logger.LogDebug("Bambu printer {DeviceId} accepted the {Command} command", DeviceId, command.Name);
        }
        finally
        {
            _replies.Forget(command);
        }
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

        _loaded = mapper.LoadedFilaments;
        _replies.TryComplete(print);

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
                .WithPayload(Payload.PushAll().Json)
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
        _loaded = BambuLoadedFilaments.None;
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
            _replies.FailAll(new NotFoundException(NotConnectedMessage));
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
