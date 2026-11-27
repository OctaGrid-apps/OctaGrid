using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OctaGrid.Connectors.BambuLab;

internal static class Payload
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    // Bambu Studio and the printer's own screen send commands too, numbered from low values; starting high keeps
    // a reply to theirs from being taken for a reply to ours.
    private static int _sequence = Random.Shared.Next(100_000, 1_000_000);

    /// <summary>Load filament from a tray (target = global tray id, 255 = unload/external depending on model).</summary>
    public static BambuCommand AmsChangeFilament(int target, int currentTemp, int targetTemp) =>
        Build("print", "ams_change_filament", b =>
        {
            b["target"] = target;
            b["curr_temp"] = currentTemp;
            b["tar_temp"] = targetTemp;
        });

    public static BambuCommand AmsControlCommand(AmsControl action) =>
        Build("print", "ams_control", b => b["param"] = action switch
        {
            AmsControl.Pause => "pause",
            AmsControl.Resume => "resume",
            AmsControl.Reset => "reset",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        });

    /// <summary>Send raw gcode. Multiple lines are separated by '\n'.</summary>
    public static BambuCommand GcodeLine(string gcode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gcode);
        var text = gcode.TrimEnd('\r', '\n') + "\n";
        return Build("print", "gcode_line", b => b["param"] = text);
    }

    // ───────────────────────── G-code ─────────────────────────
    public static BambuCommand GcodeLines(IEnumerable<string> lines) =>
        GcodeLine(string.Join("\n", lines));

    /// <summary>Request firmware/hardware module versions.</summary>
    public static BambuCommand GetVersion() => Build("info", "get_version");

    public static BambuCommand Home() => GcodeLine("G28");

    public static BambuCommand Pause() => Build("print", "pause");

    /// <summary>Print a gcode file stored on the printer. Takes an absolute path.</summary>
    public static BambuCommand PrintGcodeFile(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return Build("print", "gcode_file", b => b["param"] = fileName);
    }

    /// <summary>Print a plate from a .3mf project stored on the printer.</summary>
    public static BambuCommand PrintProjectFile(ProjectFileOptions o)
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
    public static BambuCommand PushAll() =>
        Build("pushing", "pushall", b =>
        {
            b["version"] = 1;
            b["push_target"] = 1;
        });

    public static BambuCommand Resume() => Build("print", "resume");

    /// <summary>Set filament type/color/temps on an AMS tray. Color is RRGGBBAA hex, e.g. "FF0000FF".</summary>
    public static BambuCommand SetAmsFilament(
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

    public static BambuCommand SetBedTemperature(int celsius)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(celsius);
        return GcodeLine($"M140 S{celsius}");
    }

    /// <summary>Set fan speed in percent (0–100).</summary>
    public static BambuCommand SetFanSpeed(FanType fan, int percent)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(percent, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percent, 100);
        var pwm = (int)Math.Round(percent * 255 / 100.0);
        return GcodeLine($"M106 P{(int)fan} S{pwm}");
    }

    public static BambuCommand SetLed(
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

    public static BambuCommand SetNozzleTemperature(int celsius)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(celsius);
        return GcodeLine($"M104 S{celsius}");
    }

    public static BambuCommand SetPrintSpeed(PrintSpeed speed) =>
        Build("print", "print_speed", b =>
            b["param"] = ((int)speed).ToString(CultureInfo.InvariantCulture));

    public static BambuCommand Stop() => Build("print", "stop");

    // ───────────────────────── Status / info ─────────────────────────
    // ───────────────────────── Print control ─────────────────────────
    // ───────────────────────── Lights ─────────────────────────
    // ───────────────────────── AMS / filament ─────────────────────────
    public static BambuCommand UnloadFilament() => Build("print", "unload_filament");

    private static BambuCommand Build(string category, string command, Action<JsonObject>? configure = null)
    {
        var sequenceId = NextSequenceId();
        var body = new JsonObject
        {
            ["sequence_id"] = sequenceId,
            ["command"] = command,
        };
        configure?.Invoke(body);

        return new BambuCommand(command, sequenceId, new JsonObject { [category] = body }.ToJsonString(JsonOptions));
    }

    private static string NextSequenceId() =>
        Interlocked.Increment(ref _sequence).ToString(CultureInfo.InvariantCulture);
}
