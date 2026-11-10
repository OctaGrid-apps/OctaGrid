using OctaGrid.Client.Models;
using OctaGrid.Client.Services;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Tests;

/// <summary>The Expert mode setting: off until the user agrees, and saved as soon as it is turned off.</summary>
[Collection(UiCollection.Name)]
public class ExpertModeTests
{
    private static (SettingsViewModel ViewModel, InMemorySettingsStore Store) Create(bool expertMode = false)
    {
        var store = new InMemorySettingsStore(new ClientSettings { IsExpertMode = expertMode });
        var viewModel = new SettingsViewModel(store, new ApplicationState(store), new NullUpdateService());
        return (viewModel, store);
    }

    [Fact]
    public void Expert_mode_is_off_by_default()
    {
        Assert.False(new ClientSettings().IsExpertMode);
        Assert.False(Create().Store.ClientSettings.IsExpertMode);
    }

    [Fact]
    public async Task The_saved_setting_is_shown_when_the_settings_open_and_nothing_is_changed_by_that()
    {
        var (viewModel, store) = Create(expertMode: true);

        await viewModel.LoadDataCommand.ExecuteAsync(null);

        // Showing the saved value must not ask the question again or write anything.
        Assert.True(viewModel.IsExpertMode);
        Assert.True(store.ClientSettings.IsExpertMode);
    }

    [Fact]
    public async Task Turning_Expert_mode_off_is_saved_at_once_without_a_question()
    {
        var (viewModel, store) = Create(expertMode: true);
        await viewModel.LoadDataCommand.ExecuteAsync(null);

        viewModel.IsExpertMode = false;

        Assert.False(store.ClientSettings.IsExpertMode);
    }

    [Fact]
    public async Task Turning_Expert_mode_on_is_not_saved_unless_the_user_agrees()
    {
        // The settings are not in a window here, so the question cannot be shown, which counts as "no".
        await HeadlessUi.Run(async () =>
        {
            var (viewModel, store) = Create();
            await viewModel.LoadDataCommand.ExecuteAsync(null);

            viewModel.IsExpertMode = true;

            Assert.False(store.ClientSettings.IsExpertMode);
            Assert.False(viewModel.IsExpertMode);
        });
    }
}
