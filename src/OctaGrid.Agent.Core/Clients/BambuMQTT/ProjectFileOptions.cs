namespace OctaGrid.Agent.Core.Clients.BambuMQTT;

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
