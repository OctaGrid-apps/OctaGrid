using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;
using Velopack;
using Velopack.Sources;

namespace OctaGrid.Client.Desktop.Services;

/// <summary>
/// Updates an app installed by Velopack from the GitHub releases. A downloaded update is installed when the app closes
/// (or when the user chooses to restart), so a download never interrupts someone watching a print.
/// </summary>
public sealed class VelopackUpdateService : IUpdateService
{
    private const string RepoUrl = "https://github.com/OctaGrid-apps/OctaGrid";

    private readonly ILogger<VelopackUpdateService> _logger;
    private readonly IAppSettingsStore _settings;

    // Reads what Velopack installed (version, install state) and installs a downloaded update. Checks and downloads use a
    // manager of their own, because whether they include pre-releases can change while the app runs.
    private readonly UpdateManager? _installed;

    private bool _exitHookAttached;
    private VelopackAsset? _pendingUpdate;
    private bool _restartAfterUpdate;

    public VelopackUpdateService(IAppSettingsStore settings, ILogger<VelopackUpdateService>? logger = null)
    {
        _settings = settings;
        _logger = logger ?? NullLogger<VelopackUpdateService>.Instance;

        try
        {
            _installed = new UpdateManager(new GithubSource(RepoUrl, null, false));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the Velopack installation. Updates are disabled");
        }
    }

    public UpdateChannel Channel => UpdateChannel.Velopack;

    // A run from the IDE or a portable copy is not installed by Velopack, so there is nothing to update.
    public bool CheckOnStartup => IsUpdateSupported;

    public bool IsUpdateSupported => _installed?.IsInstalled == true;

    public bool SupportsPreReleaseUpdates => IsUpdateSupported;

    // The version Velopack installed, not the assembly version: it is the version the update feed is compared with.
    public bool IsPreReleaseInstalled => _installed?.CurrentVersion?.IsPrerelease == true;

    public bool InstallsUpdateOnExit => true;

    private bool IncludePreReleases => _settings.ClientSettings.ReceivesPreReleaseUpdates(IsPreReleaseInstalled);

    private string CurrentVersion => _installed?.CurrentVersion is { } version ? $"v{version.ToFullString()}" : Helper.GetAppVersion();

    public async Task<AppUpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        var currentVersion = CurrentVersion;
        if (!IsUpdateSupported)
        {
            _logger.LogInformation("Application is not installed via Velopack (portable or dev run). Updates disabled.");
            return new AppUpdateInfo(false, currentVersion, currentVersion, "Portable or dev run does not support auto-updates.", null);
        }

        var includePreReleases = IncludePreReleases;
        _logger.LogInformation(
            "Checking for Velopack updates (current version: {Version}, pre-releases: {IncludePreReleases})",
            currentVersion,
            includePreReleases);

        try
        {
            // Without AllowVersionDowngrade a newest release older than the installed one is no update: a user who turns
            // pre-releases off stays on their version until a newer stable one ships. Going back could meet a database
            // that a newer version already changed.
            var update = await CreateManager(includePreReleases).CheckForUpdatesAsync();
            if (update is null)
            {
                _logger.LogInformation("No Velopack update available");
                return new AppUpdateInfo(false, currentVersion, currentVersion, null, null);
            }

            var targetVersion = update.TargetFullRelease.Version.ToFullString();
            _logger.LogInformation("Velopack update available: {TargetVersion}", targetVersion);
            return new AppUpdateInfo(true, currentVersion, targetVersion, update.TargetFullRelease.NotesHTML, update);
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

        var target = veloUpdate.TargetFullRelease;
        _logger.LogInformation("Downloading Velopack update {Version}", target.Version.ToFullString());
        await CreateManager(IncludePreReleases).DownloadUpdatesAsync(veloUpdate, progress => progressCallback?.Invoke(progress), cancellationToken);

        _pendingUpdate = target;
        AttachExitHook();
        _logger.LogInformation("Velopack update {Version} downloaded; it is installed when the app closes", target.Version.ToFullString());
    }

    public Task RestartToApplyUpdateAsync()
    {
        if (_pendingUpdate is null || _installed is null)
        {
            return Task.CompletedTask;
        }

        _logger.LogInformation("Restarting to install Velopack update {Version}", _pendingUpdate.Version.ToFullString());
        _restartAfterUpdate = true;

        // Closing through the lifetime shuts the app down cleanly (agent, database, logs); the exit hook then starts the
        // updater. Without a desktop lifetime there is no clean way out, so Velopack exits the process itself.
        if (_exitHookAttached && Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
        else
        {
            _installed.ApplyUpdatesAndRestart(_pendingUpdate);
        }

        return Task.CompletedTask;
    }

    public Task OpenStoreListingAsync() => Task.CompletedTask;

    private static UpdateManager CreateManager(bool includePreReleases) =>
        new(new GithubSource(RepoUrl, null, includePreReleases));

    private void AttachExitHook()
    {
        if (_exitHookAttached)
        {
            return;
        }

        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Exit += OnAppExit;
            _exitHookAttached = true;
        }
        else
        {
            // Velopack installs a downloaded update the next time the app starts (VelopackApp auto-apply), so it is not lost.
            _logger.LogWarning("No desktop lifetime to watch; the update is installed the next time the app starts");
        }
    }

    private void OnAppExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        if (_pendingUpdate is null || _installed is null)
        {
            return;
        }

        try
        {
            // The updater waits (up to 60 seconds) for this process to end, then installs. Closing the app is silent;
            // a restart the user asked for shows the updater's progress until the app is back.
            _installed.WaitExitThenApplyUpdates(_pendingUpdate, silent: !_restartAfterUpdate, restart: _restartAfterUpdate);
            _logger.LogInformation("Velopack will install update {Version} after the app exits", _pendingUpdate.Version.ToFullString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not start the Velopack updater; the update is installed the next time the app starts");
        }
    }
}
