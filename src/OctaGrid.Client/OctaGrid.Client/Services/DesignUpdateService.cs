using OctaGrid.Client.Models;

namespace OctaGrid.Client.Services;

internal sealed class DesignUpdateService : IUpdateService
{
    public UpdateChannel Channel => UpdateChannel.MicrosoftStore;

    public bool IsUpdateSupported => true;

    public Task<AppUpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new AppUpdateInfo(false, "v1.0.0", "v1.0.0", null, null));

    public Task DownloadAndApplyUpdateAsync(AppUpdateInfo updateInfo, Action<int>? progressCallback, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task OpenStoreListingAsync() => Task.CompletedTask;
}
