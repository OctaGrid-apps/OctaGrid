using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;
using Velopack;
using Velopack.Sources;

namespace OctaGrid.Client.Desktop.Services;

public sealed class VelopackUpdateService : IUpdateService
{
    private const string RepoUrl = "https://github.com/OctaGrid-apps/OctaGrid";
    private readonly ILogger<VelopackUpdateService> _logger;

    public VelopackUpdateService(ILogger<VelopackUpdateService>? logger = null)
    {
        _logger = logger ?? NullLogger<VelopackUpdateService>.Instance;
    }

    public UpdateChannel Channel => UpdateChannel.Velopack;

    public bool IsUpdateSupported
    {
        get
        {
            try
            {
                var mgr = new UpdateManager(new GithubSource(RepoUrl, null, false));
                return mgr.IsInstalled;
            }
            catch
            {
                return false;
            }
        }
    }

    public async Task<AppUpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        var currentVersion = Helper.GetAppVersion();
        _logger.LogInformation("Checking for Velopack updates (current version: {Version})", currentVersion);

        try
        {
            var mgr = new UpdateManager(new GithubSource(RepoUrl, null, false));
            if (!mgr.IsInstalled)
            {
                _logger.LogInformation("Application is not installed via Velopack (portable or dev run). Updates disabled.");
                return new AppUpdateInfo(false, currentVersion, currentVersion, "Portable or dev run does not support auto-updates.", null);
            }

            var update = await mgr.CheckForUpdatesAsync();
            if (update is null)
            {
                _logger.LogInformation("No Velopack update available");
                return new AppUpdateInfo(false, currentVersion, currentVersion, null, null);
            }

            var targetVersion = update.TargetFullRelease.Version.ToFullString();
            _logger.LogInformation("Velopack update available: {TargetVersion}", targetVersion);
            return new AppUpdateInfo(
                true,
                currentVersion,
                targetVersion,
                update.TargetFullRelease.NotesHTML,
                update);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to check for Velopack updates");
            throw;
        }
    }

    public async Task DownloadAndApplyUpdateAsync(AppUpdateInfo updateInfo, Action<int>? progressCallback, CancellationToken cancellationToken)
    {
        if (updateInfo.RawUpdate is not UpdateInfo veloUpdate)
        {
            throw new InvalidOperationException("No valid Velopack update info provided.");
        }

        _logger.LogInformation("Downloading Velopack update {Version}", veloUpdate.TargetFullRelease.Version.ToFullString());
        var mgr = new UpdateManager(new GithubSource(RepoUrl, null, false));
        await mgr.DownloadUpdatesAsync(veloUpdate, progress => progressCallback?.Invoke(progress), cancellationToken);

        _logger.LogInformation("Applying Velopack update and restarting");
        mgr.ApplyUpdatesAndRestart(veloUpdate);
    }

    public Task OpenStoreListingAsync() => Task.CompletedTask;
}
