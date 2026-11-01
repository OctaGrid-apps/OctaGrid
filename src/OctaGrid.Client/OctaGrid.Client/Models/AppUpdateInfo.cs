namespace OctaGrid.Client.Models;

public record AppUpdateInfo(
    bool IsUpdateAvailable,
    string CurrentVersion,
    string? TargetVersion,
    string? ReleaseNotes,
    object? RawUpdate);
