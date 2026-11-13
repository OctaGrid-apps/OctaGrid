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

    /// <summary>Whether the user can choose to receive pre-release updates as well as stable ones.</summary>
    bool SupportsPreReleaseUpdates => false;

    /// <summary>Whether the installed version is a pre-release. Until the user chooses, it decides whether pre-releases are offered.</summary>
    bool IsPreReleaseInstalled => false;

    /// <summary>
    /// Whether <see cref="DownloadAndApplyUpdateAsync" /> only downloads the update and leaves the app running. The update is
    /// then installed when the app closes, or right away by <see cref="RestartToApplyUpdateAsync" />.
    /// </summary>
    bool InstallsUpdateOnExit => false;

    Task<AppUpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken);

    Task DownloadAndApplyUpdateAsync(AppUpdateInfo updateInfo, Action<int>? progressCallback, CancellationToken cancellationToken);

    /// <summary>Closes the app, installs the downloaded update and starts the app again. Used when <see cref="InstallsUpdateOnExit" />.</summary>
    Task RestartToApplyUpdateAsync() => Task.CompletedTask;

    Task OpenStoreListingAsync();
}
