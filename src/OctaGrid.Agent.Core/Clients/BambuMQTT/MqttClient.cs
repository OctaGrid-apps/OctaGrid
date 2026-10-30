using System.Diagnostics;
using System.Globalization;
using System.Security.Authentication;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentFTP;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTnet;
using MQTTnet.Exceptions;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using OctaGrid.Protocol.Agents;
using OctaGrid.Protocol.Messaging;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Agent.Core.Clients.BambuMQTT;

internal enum AmsControl
{ Pause, Resume, Reset }

internal enum FanType
{ Part = 1, Aux = 2, Chamber = 3 }

internal enum LedMode
{ On, Off, Flashing }

internal enum LedNode
{ ChamberLight, WorkLight }

internal enum PrintSpeed
{ Silent = 1, Standard = 2, Sport = 3, Ludicrous = 4 }

internal static class BambuTopics
{
    public static string Report(string serial) => $"device/{serial}/report";

    public static string Request(string serial) => $"device/{serial}/request";
}

internal sealed class MqttClient : IDeviceClient, IThumbnailProvider
{
    private const int DefaultPort = 8883;
    private const string Username = "bblp";

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan InitialStateRetryInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PushAllMinInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);
    private readonly MqttClientFactory _factory = new();
    private readonly ILogger<MqttClient> _logger;
    private readonly DeviceProfile _profile;
    private volatile IMqttClient? _client;
    private long _lastPushAll;
    private volatile string? _serial;

    public MqttClient(DeviceProfile profile, ILogger<MqttClient>? logger = null)
    {
        _profile = profile;
        _logger = logger ?? NullLogger<MqttClient>.Instance;
    }

    public Guid DeviceId => _profile.DeviceId;

    public Task DeleteFileAsync(string fileName, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public ValueTask DisposeAsync()
    {
        // Each session owns and disposes its own IMqttClient, and DeviceOrchestrator awaits RunAsync
        // before disposing the device client, so there is nothing left to release here.
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    // ── Not implemented yet: this client currently only connects and reports state ──
    public async Task<IEnumerable<PrintMetadata>> GetPrinterFilesAsync(CancellationToken cancellationToken)
    {
        _profile.Settings.TryGetValue("ApiKey", out var accessCode);
        if (string.IsNullOrEmpty(accessCode))
        {
            _logger.LogWarning("Cannot list files on Bambu printer {DeviceId}: LAN access code is not configured", DeviceId);
            return [];
        }

        var ftpConfig = new FtpConfig
        {
            EncryptionMode = FtpEncryptionMode.Implicit,
            ValidateAnyCertificate = true,
            DataConnectionEncryption = true,
            SocketKeepAlive = true
        };

        _logger.LogDebug("Connecting to FTPS on Bambu printer {DeviceId} ({Endpoint}) to list files", DeviceId, _profile.ConnectionString);
        var mainFtp = new AsyncFtpClient(_profile.ConnectionString, Username, accessCode, 990, ftpConfig);
        await mainFtp.Connect(cancellationToken);
        var items = await mainFtp.GetListing("/", cancellationToken);

        var threeMfFiles = items
            .Where(i => i.Type == FtpObjectType.File &&
                        i.Name.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase) &&
                        i.Size > 0)
            .ToList();

        if (threeMfFiles.Count == 0)
        {
            _logger.LogDebug("No .3mf files found on Bambu printer {DeviceId}", DeviceId);
            await mainFtp.Disconnect(cancellationToken);
            return [];
        }

        var maxConcurrency = Math.Clamp(threeMfFiles.Count, 1, 3);
        var clients = new List<AsyncFtpClient>(maxConcurrency) { mainFtp };
        for (var i = 1; i < maxConcurrency; i++)
        {
            var extraFtp = new AsyncFtpClient(_profile.ConnectionString, Username, accessCode, 990, ftpConfig);
            await extraFtp.Connect(cancellationToken);
            clients.Add(extraFtp);
        }

        var clientQueue = new System.Collections.Concurrent.ConcurrentQueue<AsyncFtpClient>(clients);
        var results = new System.Collections.Concurrent.ConcurrentBag<PrintMetadata>();

        var sw = Stopwatch.StartNew();
        var completedCount = 0;

        await Parallel.ForEachAsync(threeMfFiles, new ParallelOptions
        {
            MaxDegreeOfParallelism = maxConcurrency,
            CancellationToken = cancellationToken
        }, async (item, ct) =>
        {
            AsyncFtpClient client;
            while (!clientQueue.TryDequeue(out client!))
            {
                await Task.Yield();
            }

            var filesw = Stopwatch.StartNew();
            try
            {
                var result = await TailThreeMfFtpReader.ExtractFromTailAsync(
                    client,
                    item,
                    extractThumbnail: true,
                    logger: _logger,
                    cancellationToken: ct);

                if (result.HasValue)
                {
                    results.Add(result.Value.Metadata);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Log warning and continue so one corrupted 3MF doesn't break the whole directory scan
                _logger.LogWarning(ex, "Failed to extract metadata from 3MF file '{FileName}' on device {DeviceId}", item.Name, DeviceId);
            }
            finally
            {
                filesw.Stop();
                var count = Interlocked.Increment(ref completedCount);
                _logger.LogDebug("Processed 3MF file #{FileIndex} '{FileName}' ({FileSize} bytes) in {ElapsedMs}ms on device {DeviceId}",
                    count, item.Name, item.Size, filesw.ElapsedMilliseconds, DeviceId);
                clientQueue.Enqueue(client);
            }
        });

        sw.Stop();
        _logger.LogInformation("Scanned {FileCount} 3MF file(s) on Bambu printer {DeviceId} in {ElapsedMs}ms",
            threeMfFiles.Count, DeviceId, sw.ElapsedMilliseconds);

        foreach (var client in clients)
        {
            try
            {
                await client.Disconnect(CancellationToken.None);
                client.Dispose();
            }
            catch { }
        }

        return results.ToList();
    }

    public async Task<PrintThumbnail?> GetThumbnailAsync(string fileName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        _profile.Settings.TryGetValue("ApiKey", out var accessCode);
        if (string.IsNullOrEmpty(accessCode))
        {
            return null;
        }

        var ftp = new AsyncFtpClient(_profile.ConnectionString, Username, accessCode, 990, new FtpConfig
        {
            EncryptionMode = FtpEncryptionMode.Implicit,
            ValidateAnyCertificate = true,
            DataConnectionEncryption = true,
            SocketKeepAlive = true
        });

        try
        {
            await ftp.Connect(cancellationToken);
            var path = "/" + fileName.TrimStart('/', '\\').Replace('\\', '/');
            var item = await ftp.GetObjectInfo(path, token: cancellationToken);
            if (item is null)
            {
                _logger.LogDebug("3MF file '{FileName}' not found on Bambu printer {DeviceId} when fetching thumbnail", fileName, DeviceId);
                return null;
            }

            var result = await TailThreeMfFtpReader.ExtractFromTailAsync(
                ftp,
                item,
                extractThumbnail: true,
                logger: _logger,
                cancellationToken: cancellationToken);

            if (result?.Thumbnail is { Length: > 0 } thumbBytes)
            {
                return new PrintThumbnail("image/png", thumbBytes);
            }

            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Failed to fetch thumbnail for '{FileName}' on Bambu printer {DeviceId}", fileName, DeviceId);
            return null;
        }
        finally
        {
            try { await ftp.Disconnect(CancellationToken.None); } catch { }
            ftp.Dispose();
        }
    }

    public Task PausePrintJobAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task ResumePrintJobAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public async Task RunAsync(ITelemetryService telemetryService, CancellationToken cancellationToken)
    {
        if (!TryGetEndpoint(out var host, out var port))
        {
            _logger.LogWarning("Bambu printer {DeviceId} has invalid endpoint '{Endpoint}'", DeviceId, _profile.ConnectionString);
            ReportOffline(telemetryService, $"'{_profile.ConnectionString}' is not a valid printer address.");
            await WaitUntilCancelledAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        _profile.Settings.TryGetValue("ApiKey", out var accessCode);
        if (string.IsNullOrEmpty(accessCode))
        {
            _logger.LogWarning("Bambu printer {DeviceId} ({Host}:{Port}) is missing LAN access code", DeviceId, host, port);
            ReportOffline(telemetryService, "The printer's LAN access code is not set.");
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
                established = await RunSessionAsync(host, port, accessCode, telemetryService, cancellationToken)
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
                ReportOffline(telemetryService, failure);
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

    public Task StartPrintJobAsync(string fileName, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task StopPrintJobAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    // The result arrives through telemetry rather than as a return value, so — as in
    // MoonrakerClient — the caller's token deliberately doesn't cancel it: a UI command being
    // superseded must not abort a request that is already on its way.
    public Task TriggerInitialStateAsync(CancellationToken cancellationToken)
    {
        var client = _client;
        return client is null ? Task.CompletedTask : RequestFullStateAsync(client, CancellationToken.None);
    }

    public Task UploadFileAsync(Stream stream, string fileName, CancellationToken cancellationToken)
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
            ITelemetryService telemetryService,
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
        firstReport.TrySetResult();

        if (changed)
        {
            telemetryService.Send(mapper.Snapshot());
        }
    }

    // The reason goes out as the one entry of StateMessages, which the card lists. The next report
    // carries an empty list, so it clears itself as soon as the printer is back.
    private void ReportOffline(ITelemetryService telemetryService, string reason) =>
        telemetryService.Send(new PrinterTelemetry(DeviceId)
        {
            State = UniversalPrinterState.Offline,
            StateMessages = new Stack<PrinterStateMessage>([new PrinterStateMessage(reason)])
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
    private async Task<bool> RunSessionAsync(string host, int port, string accessCode, ITelemetryService telemetryService, CancellationToken ct)
    {
        using var client = _factory.CreateMqttClient();
        var mapper = new BambuReportMapper(DeviceId);
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstReport = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _serial = _profile.Settings.GetValueOrDefault("SerialNumber") is { Length: > 0 } configured ? configured : null;
        Interlocked.Exchange(ref _lastPushAll, 0);

        // Handlers must never throw into MQTTnet's receive loop.
        client.ApplicationMessageReceivedAsync += e =>
        {
            try
            {
                OnMessage(e, client, mapper, telemetryService, firstReport, ct);
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
        if (!Uri.TryCreate($"mqtts://{_profile.ConnectionString}", UriKind.Absolute, out var uri) || uri.Host.Length == 0)
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

/// <summary>Options for the "project_file" print command (.3mf).</summary>
internal sealed record ProjectFileOptions
{
    /// <summary>Path of the gcode inside the 3mf, e.g. "Metadata/plate_1.gcode".</summary>
    public string Param { get; init; } = "Metadata/plate_1.gcode";

    /// <summary>Display name of the job.</summary>
    public string SubtaskName { get; init; } = "";

    /// <summary>Full file URL, e.g. "file:///sdcard/model.3mf" or "ftp://model.3mf".</summary>
    public required string Url { get; init; }

    /// <summary>Optional file name (not needed if <see cref="Url"/> contains the full path).</summary>
    public string File { get; init; } = "";

    public string Md5 { get; init; } = "";

    public bool Timelapse { get; init; }
    public string BedType { get; init; } = "auto";
    public bool BedLevelling { get; init; } = true;
    public bool FlowCalibration { get; init; } = true;
    public bool VibrationCalibration { get; init; } = true;
    public bool LayerInspect { get; init; } = true;

    public bool UseAms { get; init; }

    /// <summary>
    /// Filament -> AMS slot mapping, index = filament in the project, value = global tray id
    /// (-1 = unused). Example: [0, -1, -1, -1]. Only used when <see cref="UseAms"/> is true.
    /// </summary>
    public IReadOnlyList<int>? AmsMapping { get; init; }

    public static ProjectFileOptions FromSdCard(string fileName, int plate = 1) => new()
    {
        Param = $"Metadata/plate_{plate}.gcode",
        Url = $"file:///sdcard/{fileName}",
        SubtaskName = Path.GetFileNameWithoutExtension(fileName),
    };
}

internal static class Payload
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    private static int _sequence;

    /// <summary>Load filament from a tray (target = global tray id, 255 = unload/external depending on model).</summary>
    public static string AmsChangeFilament(int target, int currentTemp, int targetTemp) =>
        Build("print", "ams_change_filament", b =>
        {
            b["target"] = target;
            b["curr_temp"] = currentTemp;
            b["tar_temp"] = targetTemp;
        });

    public static string AmsControlCommand(AmsControl action) =>
        Build("print", "ams_control", b => b["param"] = action switch
        {
            AmsControl.Pause => "pause",
            AmsControl.Resume => "resume",
            AmsControl.Reset => "reset",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        });

    /// <summary>Send raw gcode. Multiple lines are separated by '\n'.</summary>
    public static string GcodeLine(string gcode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gcode);
        var text = gcode.TrimEnd('\r', '\n') + "\n";
        return Build("print", "gcode_line", b => b["param"] = text);
    }

    // ───────────────────────── G-code ─────────────────────────
    public static string GcodeLines(IEnumerable<string> lines) =>
        GcodeLine(string.Join("\n", lines));

    /// <summary>Request firmware/hardware module versions.</summary>
    public static string GetVersion() => Build("info", "get_version");

    public static string Home() => GcodeLine("G28");

    public static string Pause() => Build("print", "pause");

    /// <summary>Print a gcode file stored on the printer. Takes an absolute path.</summary>
    public static string PrintGcodeFile(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return Build("print", "gcode_file", b => b["param"] = fileName);
    }

    /// <summary>Print a plate from a .3mf project stored on the printer.</summary>
    public static string PrintProjectFile(ProjectFileOptions o)
    {
        ArgumentNullException.ThrowIfNull(o);

        return Build("print", "project_file", b =>
        {
            b["param"] = o.Param;

            // Always "0" for local prints
            b["project_id"] = "0";
            b["profile_id"] = "0";
            b["task_id"] = "0";
            b["subtask_id"] = "0";
            b["subtask_name"] = o.SubtaskName;

            b["file"] = o.File;
            b["url"] = o.Url;
            b["md5"] = o.Md5;

            b["timelapse"] = o.Timelapse;
            b["bed_type"] = o.BedType;
            b["bed_levelling"] = o.BedLevelling;
            b["flow_cali"] = o.FlowCalibration;
            b["vibration_cali"] = o.VibrationCalibration;
            b["layer_inspect"] = o.LayerInspect;

            b["use_ams"] = o.UseAms;
            b["ams_mapping"] = o.UseAms && o.AmsMapping is { Count: > 0 }
                ? new JsonArray(o.AmsMapping.Select(i => (JsonNode)JsonValue.Create(i)).ToArray())
                : (JsonNode)"";
        });
    }

    /// <summary>Request full status report from the printer.</summary>
    public static string PushAll() =>
        Build("pushing", "pushall", b =>
        {
            b["version"] = 1;
            b["push_target"] = 1;
        });

    public static string Resume() => Build("print", "resume");

    /// <summary>Set filament type/color/temps on an AMS tray. Color is RRGGBBAA hex, e.g. "FF0000FF".</summary>
    public static string SetAmsFilament(
        int amsId,
        int trayId,
        string filamentType,
        string colorRrggbbaa,
        int nozzleTempMin,
        int nozzleTempMax,
        string trayInfoIdx = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filamentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(colorRrggbbaa);

        return Build("print", "ams_filament_setting", b =>
        {
            b["ams_id"] = amsId;
            b["tray_id"] = trayId;
            b["tray_info_idx"] = trayInfoIdx;
            b["tray_type"] = filamentType;
            b["tray_color"] = colorRrggbbaa;
            b["nozzle_temp_min"] = nozzleTempMin;
            b["nozzle_temp_max"] = nozzleTempMax;
            b["setting_id"] = "";
        });
    }

    public static string SetBedTemperature(int celsius)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(celsius);
        return GcodeLine($"M140 S{celsius}");
    }

    /// <summary>Set fan speed in percent (0–100).</summary>
    public static string SetFanSpeed(FanType fan, int percent)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(percent, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percent, 100);
        var pwm = (int)Math.Round(percent * 255 / 100.0);
        return GcodeLine($"M106 P{(int)fan} S{pwm}");
    }

    public static string SetLed(
        LedNode node,
        LedMode mode,
        int onTimeMs = 500,
        int offTimeMs = 500,
        int loopTimes = 0,
        int intervalMs = 0) =>
        Build("system", "ledctrl", b =>
        {
            b["led_node"] = node switch
            {
                LedNode.ChamberLight => "chamber_light",
                LedNode.WorkLight => "work_light",
                _ => throw new ArgumentOutOfRangeException(nameof(node)),
            };
            b["led_mode"] = mode switch
            {
                LedMode.On => "on",
                LedMode.Off => "off",
                LedMode.Flashing => "flashing",
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            };
            b["led_on_time"] = onTimeMs;
            b["led_off_time"] = offTimeMs;
            b["loop_times"] = loopTimes;
            b["interval_time"] = intervalMs;
        });

    public static string SetNozzleTemperature(int celsius)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(celsius);
        return GcodeLine($"M104 S{celsius}");
    }

    public static string SetPrintSpeed(PrintSpeed speed) =>
        Build("print", "print_speed", b =>
            b["param"] = ((int)speed).ToString(CultureInfo.InvariantCulture));

    public static string Stop() => Build("print", "stop");

    // ───────────────────────── Status / info ─────────────────────────
    // ───────────────────────── Print control ─────────────────────────
    // ───────────────────────── Lights ─────────────────────────
    // ───────────────────────── AMS / filament ─────────────────────────
    public static string UnloadFilament() => Build("print", "unload_filament");

    private static string Build(string category, string command, Action<JsonObject>? configure = null)
    {
        var body = new JsonObject
        {
            ["sequence_id"] = NextSequenceId(),
            ["command"] = command,
        };
        configure?.Invoke(body);

        return new JsonObject { [category] = body }.ToJsonString(JsonOptions);
    }

    private static string NextSequenceId() =>
        Interlocked.Increment(ref _sequence).ToString(CultureInfo.InvariantCulture);
}
