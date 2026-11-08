using OctaGrid.Client.Models;

namespace OctaGrid.Client.Services;

public interface IUpdateService
{
    UpdateChannel Channel { get; }

    bool IsUpdateSupported { get; }

    /// <summary>
    /// Whether the app should look for an update by itself when it starts. Development builds turn this off so
    /// repeated runs do not exhaust the GitHub API rate limit; the manual check in Settings still works.
    /// </summary>
    bool CheckOnStartup => true;

    Task<AppUpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken);

    Task DownloadAndApplyUpdateAsync(AppUpdateInfo updateInfo, Action<int>? progressCallback, CancellationToken cancellationToken);

    Task OpenStoreListingAsync();
}
