using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Extensions.Abstractions;
using OctaGrid.Protocol.Messaging;
using StreamJsonRpc;

namespace OctaGrid.Connectors.Moonraker;

public class MoonrakerListener
{
    private readonly Guid _deviceId;
    private readonly ILogger<MoonrakerListener> _logger;
    private readonly PrinterTelemetry _printerTelemetry;
    private readonly ITelemetrySink _telemetry;
    private string? _printState;
    private string? _webhooksMessage;
    private string? _webhooksState;

    public MoonrakerListener(Guid deviceId, ITelemetrySink telemetry, ILogger<MoonrakerListener>? logger = null)
    {
        _deviceId = deviceId;
        _telemetry = telemetry;
        _logger = logger ?? NullLogger<MoonrakerListener>.Instance;
        _printerTelemetry = new PrinterTelemetry(_deviceId);
    }

    // The file/thumbnail flow (MoonrakerFileClient) asks for this instead of duplicating the
    // print_stats tracking that already happens below.
    public string? CurrentFileName => _printerTelemetry.PrintStatus.FileName;

    /// <summary>
    /// Folds server.files.metadata's "estimated_time" into telemetry. Called by MoonrakerFileClient
    /// after a metadata query made for the thumbnail flow — this is the only place PrinterTelemetry
    /// is mutated, so the file client itself stays free of telemetry concerns.
    /// </summary>
    public void OnFileMetadata(double? estimatedTimeSeconds)
    {
        if (estimatedTimeSeconds is not { } seconds)
        {
            return;
        }

        var value = TimeOnly.FromTimeSpan(TimeSpan.FromSeconds(seconds));
        if (_printerTelemetry.PrintStatus.EstimatedPrintTime != value)
        {
            _printerTelemetry.PrintStatus.EstimatedPrintTime = value;
            _telemetry.Send(Snapshot());
        }
    }

    [JsonRpcMethod("notify_gcode_response")]
    public void OnGcodeResponse(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.String)
        {
            return;
        }

        // The G-code console (every "ok" and temperature echo) is a log, not printer state, so it is
        // deliberately not forwarded; see the planned console capability.
    }

    [JsonRpcMethod("notify_klippy_disconnected")]
    public void OnKlippyDisconnected()
    {
        _logger.LogWarning("Klippy disconnected from Moonraker on device {DeviceId}", _deviceId);
    }

    [JsonRpcMethod("notify_klippy_ready")]
    public void OnKlippyReady()
    {
        _logger.LogInformation("Klippy is ready on Moonraker device {DeviceId}", _deviceId);
    }

    [JsonRpcMethod("notify_klippy_shutdown")]
    public void OnKlippyShutdown()
    {
        _logger.LogWarning("Klippy has entered shutdown state on Moonraker device {DeviceId}", _deviceId);
    }

    [JsonRpcMethod("notify_proc_stat_update")]
    public void OnProcStatusUpdate(JsonElement status)
    {
    }

    [JsonRpcMethod("notify_status_update")]
    public void OnStatusUpdate(JsonElement payload, double eventTime)
    {
        var status = payload.TryGetProperty("status", out var inner) ? inner : payload;
        if (status.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var t = _printerTelemetry;
        var changed = false;
        var stateChanged = false;

        if (status.TryGetProperty("webhooks", out var webhooks))
        {
            stateChanged |= webhooks.Update("state", _webhooksState, v => _webhooksState = v);
            webhooks.Update("state_message", _webhooksMessage, v => _webhooksMessage = v);

            // Update() reports "present", not "different", so compare before announcing a change.
            var conditions = ToConditions(_webhooksState, _webhooksMessage);
            if (!conditions.SequenceEqual(t.Conditions))
            {
                t.Conditions = conditions;
                changed = true;
            }
        }

        if (status.TryGetProperty("extruder", out var extruder))
        {
            changed |= extruder.Update("temperature", t.NozzleTemperature, v => t.NozzleTemperature = v);
            changed |= extruder.Update("target", t.NozzleTargetTemperature, v => t.NozzleTargetTemperature = v);
        }

        if (status.TryGetProperty("heater_bed", out var bed))
        {
            changed |= bed.Update("temperature", t.BedTemperature, v => t.BedTemperature = v);
            changed |= bed.Update("target", t.BedTargetTemperature, v => t.BedTargetTemperature = v);
        }

        if (status.TryGetProperty("display_status", out var display))
        {
            changed |= display.Update("progress", t.PrintStatus.Progress, v => t.PrintStatus.Progress = v, digits: 3);
        }

        if (status.TryGetProperty("print_stats", out var print))
        {
            changed |= print.Update("filename", t.PrintStatus.FileName, v => t.PrintStatus.FileName = v);
            changed |= print.Update("print_duration", t.PrintStatus.PrintTime, v => t.PrintStatus.PrintTime = v);
            changed |= print.Update("total_duration", t.PrintStatus.PrintTimeTotal, v => t.PrintStatus.PrintTimeTotal = v);
            stateChanged |= print.Update("state", _printState, v => _printState = v);

            if (print.TryGetProperty("info", out var info))
            {
                changed |= info.Update("total_layer", t.PrintStatus.LayersCount, v => t.PrintStatus.LayersCount = v);
                changed |= info.Update("current_layer", t.PrintStatus.CurrentLayer, v => t.PrintStatus.CurrentLayer = v);
            }
        }

        if (stateChanged)
        {
            // Named: both are plain strings, so a swapped call would compile and silently misreport.
            var printerState = MoonrakerJson.ToPrinterState(printStatsState: _printState, klippyState: _webhooksState);
            if (t.State != printerState)
            {
                _logger.LogInformation("Moonraker printer {DeviceId} state changed: {OldState} -> {NewState}", _deviceId, t.State, printerState);
                t.State = printerState;
                changed = true;
            }
        }

        if (changed)
        {
            _telemetry.Send(Snapshot());
        }
    }

    // Klippy reports "Printer is ready" as its message while healthy; only a not-ready state with a
    // message is something the user has to act on.
    private static IReadOnlyList<PrinterCondition> ToConditions(string? klippyState, string? message)
    {
        if (string.IsNullOrWhiteSpace(message) || string.Equals(klippyState, "ready", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var severity = string.Equals(klippyState, "startup", StringComparison.OrdinalIgnoreCase)
            ? PrinterSeverity.Warning
            : PrinterSeverity.Error;
        return [new PrinterCondition("klippy.state", severity, message.Trim())];
    }

    // Subscribers get a copy: this listener keeps updating its own instance from another thread.
    private PrinterTelemetry Snapshot() =>
        _printerTelemetry with { PrintStatus = _printerTelemetry.PrintStatus with { } };
}
