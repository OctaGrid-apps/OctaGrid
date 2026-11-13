using OctaGrid.Client.Models;
using OctaGrid.Client.Services;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Tests;

/// <summary>
/// Velopack-style updates: the download leaves the app running and the update is installed when the app closes or on a
/// restart the user chooses; the user decides whether pre-release versions are offered.
/// </summary>
public class UpdateOnExitTests
{
    private sealed class FakeVelopackService : IUpdateService
    {
        public UpdateChannel Channel => UpdateChannel.Velopack;

        public bool IsUpdateSupported => true;

        public bool SupportsPreReleaseUpdates => true;

        public bool IsPreReleaseInstalled { get; init; }

        public bool InstallsUpdateOnExit => true;

        public int Checks { get; private set; }

        public int Downloads { get; private set; }

        public int Restarts { get; private set; }

        public Task<AppUpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken)
        {
            Checks++;
            return Task.FromResult(new AppUpdateInfo(true, "1.0.0", "1.1.0", null, null));
        }

        public Task DownloadAndApplyUpdateAsync(AppUpdateInfo updateInfo, Action<int>? progressCallback, CancellationToken cancellationToken)
        {
            Downloads++;
            progressCallback?.Invoke(100);
            return Task.CompletedTask;
        }

        public Task RestartToApplyUpdateAsync()
        {
            Restarts++;
            return Task.CompletedTask;
        }

        public Task OpenStoreListingAsync() => Task.CompletedTask;
    }

    private static (SettingsViewModel ViewModel, FakeVelopackService Service, InMemorySettingsStore Store) Create(
        bool preReleaseInstalled = false,
        bool? receivePreReleases = null)
    {
        var service = new FakeVelopackService { IsPreReleaseInstalled = preReleaseInstalled };
        var store = new InMemorySettingsStore(new ClientSettings { ReceivePreReleaseUpdates = receivePreReleases });
        var state = new ApplicationState(store)
        {
            AvailableUpdate = new AppUpdateInfo(true, "1.0.0", "1.1.0", null, null),
        };
        return (new SettingsViewModel(store, state, service), service, store);
    }

    [Theory]
    [InlineData(null, false, false)]
    [InlineData(null, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    public void The_users_choice_wins_and_until_then_the_installed_version_decides(bool? choice, bool preReleaseInstalled, bool expected)
    {
        var settings = new ClientSettings { ReceivePreReleaseUpdates = choice };

        Assert.Equal(expected, settings.ReceivesPreReleaseUpdates(preReleaseInstalled));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_pre_release_switch_starts_from_the_installed_version(bool preReleaseInstalled)
    {
        var (viewModel, service, store) = Create(preReleaseInstalled);

        await viewModel.LoadDataCommand.ExecuteAsync(null);

        Assert.True(viewModel.ShowPreReleaseUpdates);
        Assert.Equal(preReleaseInstalled, viewModel.ReceivePreReleaseUpdates);
        Assert.Null(store.ClientSettings.ReceivePreReleaseUpdates);
        Assert.Equal(0, service.Checks);
    }

    [Fact]
    public async Task Turning_pre_releases_off_stores_the_choice_and_checks_again()
    {
        var (viewModel, service, store) = Create(preReleaseInstalled: true);
        await viewModel.LoadDataCommand.ExecuteAsync(null);

        viewModel.ReceivePreReleaseUpdates = false;
        await viewModel.CheckForUpdatesCommand.ExecutionTask!;

        Assert.False(store.ClientSettings.ReceivePreReleaseUpdates);
        Assert.Equal(1, service.Checks);
        Assert.Equal(UpdateStatus.UpdateAvailable, viewModel.UpdateStatus);
    }

    [Fact]
    public async Task A_finished_download_waits_for_the_app_to_close_and_can_restart_now()
    {
        var (viewModel, service, _) = Create();
        Assert.Equal("Download update", viewModel.UpdateActionText);

        await viewModel.ApplyUpdateCommand.ExecuteAsync(null);

        Assert.Equal(UpdateStatus.ReadyToRestart, viewModel.UpdateStatus);
        Assert.Contains("when you close OctaGrid", viewModel.UpdateStatusText);
        Assert.Equal("Restart to apply", viewModel.UpdateActionText);
        Assert.False(viewModel.CheckForUpdatesCommand.CanExecute(null));
        Assert.False(viewModel.CanChangePreReleaseUpdates);
        Assert.Equal(0, service.Restarts);

        await viewModel.ApplyUpdateCommand.ExecuteAsync(null);

        Assert.Equal(1, service.Restarts);
        Assert.Equal(1, service.Downloads);
    }
}
