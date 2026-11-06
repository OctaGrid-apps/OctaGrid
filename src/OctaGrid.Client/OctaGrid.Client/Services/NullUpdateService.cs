using OctaGrid.Client.Models;

namespace OctaGrid.Client.Services;

/// <summary>
/// The update service for builds that cannot update themselves (and for the designer): it never
/// finds an update and has nothing to apply.
/// </summary>
public sealed class NullUpdateService : IUpdateService
{
    public UpdateChannel Channel => UpdateChannel.None;

    public bool IsUpdateSupported => false;

    public Task<AppUpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        var version = Helper.GetAppVersion();
        return Task.FromResult(new AppUpdateInfo(false, version, version, null, null));
    }

    public Task DownloadAndApplyUpdateAsync(AppUpdateInfo updateInfo, Action<int>? progressCallback, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task OpenStoreListingAsync() => Task.CompletedTask;
}
