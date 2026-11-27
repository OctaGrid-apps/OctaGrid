using OctaGrid.Extensions.Models;

namespace OctaGrid.Connectors.BambuLab;

/// <summary>
/// The options the user can change when starting a Bambu print, with Bambu Studio's defaults. Layer inspection and the
/// bed type are left to the printer and the file.
/// </summary>
internal static class BambuStartOptions
{
    public const string BedLevelling = "bed_levelling";
    public const string FlowCalibration = "flow_cali";
    public const string VibrationCalibration = "vibration_cali";
    public const string Timelapse = "timelapse";

    /// <summary>The options as the start dialog shows them.</summary>
    public static IReadOnlyList<SettingField> Fields { get; } =
    [
        Toggle(BedLevelling, "Bed levelling", true, "Probes the bed before printing."),
        Toggle(FlowCalibration, "Flow calibration", true, "Measures the filament flow before printing, on printers that can."),
        Toggle(VibrationCalibration, "Vibration calibration", true, "Tunes the motion system before printing."),
        Toggle(Timelapse, "Timelapse", false, "Records a timelapse to the SD card."),
    ];

    /// <summary>
    /// Applies the user's values to <paramref name="options"/>. Options left out keep their defaults; an unknown option
    /// or a value other than "true" or "false" is a problem.
    /// </summary>
    public static bool TryApply(
        ProjectFileOptions options,
        IReadOnlyDictionary<string, string>? values,
        out ProjectFileOptions applied,
        out string problem)
    {
        applied = options;
        problem = string.Empty;
        foreach (var (key, value) in values ?? new Dictionary<string, string>())
        {
            if (!bool.TryParse(value, out var on))
            {
                problem = $"The start option '{key}' must be true or false.";
                return false;
            }

            switch (key)
            {
                case BedLevelling:
                    applied = applied with { BedLevelling = on };
                    break;
                case FlowCalibration:
                    applied = applied with { FlowCalibration = on };
                    break;
                case VibrationCalibration:
                    applied = applied with { VibrationCalibration = on };
                    break;
                case Timelapse:
                    applied = applied with { Timelapse = on };
                    break;
                default:
                    problem = $"Bambu printers have no start option '{key}'.";
                    return false;
            }
        }

        return true;
    }

    private static SettingField Toggle(string key, string label, bool on, string help) =>
        new(key, label, SettingFieldType.Boolean, Required: false, DefaultValue: on ? "true" : "false", Help: help);
}
