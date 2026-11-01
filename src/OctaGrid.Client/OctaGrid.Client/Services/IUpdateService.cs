using OctaGrid.Client.Models;

namespace OctaGrid.Client.Services;

public interface IUpdateService
{
    UpdateChannel Channel { get; }

    bool IsUpdateSupported { get; }

    Task<AppUpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken);

    Task DownloadAndApplyUpdateAsync(AppUpdateInfo updateInfo, Action<int>? progressCallback, CancellationToken cancellationToken);

    Task OpenStoreListingAsync();
}
