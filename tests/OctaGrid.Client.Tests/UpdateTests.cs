using OctaGrid.Client.Models;
using OctaGrid.Client.Services;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Tests;

public class UpdateTests
{
    private sealed class FakeUpdateService : IUpdateService
    {
        private readonly TaskCompletionSource _downloadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public UpdateChannel Channel { get; init; } = UpdateChannel.Velopack;

        public bool IsUpdateSupported => true;

        public Task DownloadStarted => _downloadStarted.Task;

        public Task<AppUpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AppUpdateInfo(true, "1.0.0", "1.1.0", null, null));

        /// <summary>Reports 40% and then waits until the download is cancelled.</summary>
        public async Task DownloadAndApplyUpdateAsync(AppUpdateInfo updateInfo, Action<int>? progressCallback, CancellationToken cancellationToken)
        {
            progressCallback?.Invoke(40);
            _downloadStarted.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        public Task OpenStoreListingAsync() => Task.CompletedTask;
    }

    private static (SettingsViewModel ViewModel, FakeUpdateService Service) Create(UpdateChannel channel = UpdateChannel.Velopack)
    {
        var service = new FakeUpdateService { Channel = channel };
        var state = new ApplicationState(new InMemorySettingsStore())
        {
            AvailableUpdate = new AppUpdateInfo(true, "1.0.0", "1.1.0", "<p>Notes</p>", null),
        };
        return (new SettingsViewModel(new InMemorySettingsStore(), state, service), service);
    }

    [Fact]
    public async Task Checking_for_updates_is_disabled_while_the_update_downloads()
    {
        var (viewModel, service) = Create();
        Assert.True(viewModel.CheckForUpdatesCommand.CanExecute(null));

        var update = viewModel.ApplyUpdateCommand.ExecuteAsync(null);
        await service.DownloadStarted;

        Assert.False(viewModel.CheckForUpdatesCommand.CanExecute(null));
        Assert.False(viewModel.ApplyUpdateCommand.CanExecute(null));

        viewModel.ApplyUpdateCancelCommand.Execute(null);
        await update;

        Assert.True(viewModel.CheckForUpdatesCommand.CanExecute(null));
    }

    [Fact]
    public async Task Cancelling_the_download_returns_to_an_available_update()
    {
        var (viewModel, service) = Create();

        var update = viewModel.ApplyUpdateCommand.ExecuteAsync(null);
        await service.DownloadStarted;
        Assert.True(viewModel.CanCancelUpdate);

        viewModel.ApplyUpdateCancelCommand.Execute(null);
        await update;

        Assert.False(viewModel.IsUpdating);
        Assert.False(viewModel.CanCancelUpdate);
        Assert.Equal(UpdateStatus.UpdateAvailable, viewModel.UpdateStatus);
        Assert.Equal(0, viewModel.UpdateProgress);
        Assert.Contains("cancelled", viewModel.UpdateStatusText);
        Assert.True(viewModel.ApplyUpdateCommand.CanExecute(null));
    }

    [Fact]
    public async Task The_microsoft_store_update_cannot_be_cancelled()
    {
        var (viewModel, service) = Create(UpdateChannel.MicrosoftStore);

        var update = viewModel.ApplyUpdateCommand.ExecuteAsync(null);
        await service.DownloadStarted;

        Assert.True(viewModel.IsUpdating);
        Assert.False(viewModel.CanCancelUpdate);

        viewModel.ApplyUpdateCancelCommand.Execute(null);
        await update;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_release_notes_page_takes_its_colors_from_the_theme_and_leaves_the_background_to_the_dialog(bool dark)
    {
        var html = SettingsViewModel.BuildReleaseNotesHtml("<p>Notes</p>", dark, "#112233", "#445566");

        Assert.Contains("color: #112233", html);
        Assert.Contains("a { color: #445566", html);
        Assert.Contains($"content=\"{(dark ? "dark" : "light")}\"", html);
        Assert.Contains("<p>Notes</p>", html);

        // The dialog's DefaultBackground paints the window and the web view; the page must not cover it.
        Assert.DoesNotContain("background:", html.Replace("color-mix", string.Empty).Split("code, pre")[0]);
    }
}
