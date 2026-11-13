using System.Text.Json;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Connectors.BambuLab;

/// <summary>
/// Folds Bambu "report" messages into one running <see cref="PrinterTelemetry"/>. Kept apart from
/// MqttClient so that class stays about the connection lifecycle only (the same split as
/// MoonrakerClient / MoonrakerListener).
/// <para>
/// A full report arrives in answer to "pushall"; after that the printer only sends the fields that
/// changed, so every field here is optional and absent ones keep their previous value.
/// </para>
/// </summary>
internal sealed class BambuReportMapper(Guid deviceId)
{
    private readonly PrinterTelemetry _telemetry = new(deviceId);

    private string? _gcodeFile;
    private string? _gcodeState;
    private int? _layer;
    private bool _reported;
    private string? _subtaskName;
    private int? _totalLayers;

    /// <summary>Applies the "print" object of a report. Returns true if anything visible changed.</summary>
    public bool Apply(JsonElement print)
    {
        var t = _telemetry;
        var p = t.PrintStatus;
        var changed = false;

        // Any report proves the printer is reachable. gcode_state (below) refines this, but a
        // partial report may not include it, and Offline is only ever set by the client itself.
        if (!_reported)
        {
            _reported = true;
            t.State = UniversalPrinterState.Idle;
            changed = true;
        }

        changed |= Update(print, "nozzle_temper", t.NozzleTemperature, v => t.NozzleTemperature = v);
        changed |= Update(print, "nozzle_target_temper", t.NozzleTargetTemperature, v => t.NozzleTargetTemperature = v);
        changed |= Update(print, "bed_temper", t.BedTemperature, v => t.BedTemperature = v);
        changed |= Update(print, "bed_target_temper", t.BedTargetTemperature, v => t.BedTargetTemperature = v);

        // mc_percent is 0..100; the telemetry contract (and the progress bar) is 0..1.
        if (TryGetNumber(print, "mc_percent", out var percent))
        {
            var progress = Math.Round(percent / 100d, 3);
            if (p.Progress != progress)
            {
                p.Progress = progress;
                changed = true;
            }
        }

        if (TryGetNumber(print, "layer_num", out var layer))
        {
            _layer = (int)layer;
        }

        if (TryGetNumber(print, "total_layer_num", out var totalLayers))
        {
            _totalLayers = (int)totalLayers;
        }

        // Bambu reports 0/0 when idle; the card shows "---" for null, which is what that means.
        var layersCount = _totalLayers is > 0 ? _totalLayers : null;
        var currentLayer = layersCount is null ? null : _layer;
        if (p.LayersCount != layersCount || p.CurrentLayer != currentLayer)
        {
            p.LayersCount = layersCount;
            p.CurrentLayer = currentLayer;
            changed = true;
        }

        if (TryGetString(print, "subtask_name", out var subtask))
        {
            _subtaskName = subtask;
        }

        if (TryGetString(print, "gcode_file", out var gcodeFile))
        {
            _gcodeFile = gcodeFile;
        }

        // subtask_name is the project name the user recognises; gcode_file is often an internal
        // path like /data/Metadata/plate_1.gcode, so it's only the fallback.
        var fileName = !string.IsNullOrWhiteSpace(_subtaskName) ? _subtaskName
            : !string.IsNullOrWhiteSpace(_gcodeFile) ? _gcodeFile
            : null;
        if (p.FileName != fileName)
        {
            p.FileName = fileName;
            changed = true;
        }

        if (TryGetString(print, "gcode_state", out var gcodeState))
        {
            _gcodeState = gcodeState;
            var state = ToPrinterState(_gcodeState);
            if (t.State != state)
            {
                t.State = state;
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>A copy that is safe to hand to another thread while this mapper keeps updating.</summary>
    public PrinterTelemetry Snapshot() =>
        _telemetry with { PrintStatus = _telemetry.PrintStatus with { } };

    // gcode_state describes the JOB (IDLE | PREPARE | RUNNING | PAUSE | FINISH | FAILED), so — as
    // with Moonraker's print_stats — a failed job is Failed, never the printer-level Error: the
    // printer is fine and can take the next print.
    private static UniversalPrinterState ToPrinterState(string? gcodeState) =>
        gcodeState?.ToUpperInvariant() switch
        {
            "RUNNING" => UniversalPrinterState.Printing,
            "PAUSE" => UniversalPrinterState.Paused,
            "PREPARE" or "SLICING" => UniversalPrinterState.Preparing,
            "FINISH" => UniversalPrinterState.Completed,
            "FAILED" => UniversalPrinterState.Failed,
            _ => UniversalPrinterState.Idle // "IDLE", or a value this version doesn't know about
        };

    private static bool TryGetNumber(JsonElement element, string name, out double value)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetDouble(out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetString(JsonElement element, string name, out string? value)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString();
            return true;
        }

        value = null;
        return false;
    }

    private static bool Update(JsonElement element, string name, double? current, Action<double> apply)
    {
        if (!TryGetNumber(element, name, out var raw))
        {
            return false;
        }

        // One decimal, like the Moonraker side, so sensor jitter doesn't produce a message per tick.
        var value = Math.Round(raw, 1);
        if (current == value)
        {
            return false;
        }

        apply(value);
        return true;
    }
}
