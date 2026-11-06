using OctaGrid.Client.Models;
using OctaGrid.Client.Services;

namespace OctaGrid.Client.Tests;

public class SettingsStoreTests
{
    [Fact]
    public void Starts_with_default_settings()
    {
        var store = new InMemorySettingsStore();

        Assert.Equal(new ClientSettings(), store.ClientSettings);
        Assert.Equal(new ClientSettings(), store.StartupSettings);
    }

    [Fact]
    public void Starts_with_the_given_settings()
    {
        var initial = new ClientSettings { ElementTheme = ElementTheme.Dark };

        var store = new InMemorySettingsStore(initial);

        Assert.Equal(ElementTheme.Dark, store.ClientSettings.ElementTheme);
        Assert.Equal(ElementTheme.Dark, store.StartupSettings.ElementTheme);
    }

    [Fact]
    public void Update_applies_the_change_and_raises_the_event_once()
    {
        var store = new InMemorySettingsStore();
        var raised = new List<ClientSettings>();
        store.SettingsChanged += (_, settings) => raised.Add(settings);

        store.Update(s => s with { AskToSelectClientMode = true });

        Assert.True(store.ClientSettings.AskToSelectClientMode);
        Assert.Equal(store.ClientSettings, Assert.Single(raised));
    }

    [Fact]
    public void Update_that_changes_nothing_raises_no_event()
    {
        var store = new InMemorySettingsStore();
        var raised = 0;
        store.SettingsChanged += (_, _) => raised++;

        store.Update(s => s with { ElementTheme = s.ElementTheme });

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Startup_settings_keep_the_values_from_launch()
    {
        var store = new InMemorySettingsStore();

        store.Update(s => s with { Mode = ClientMode.Remote });

        Assert.Equal(ClientMode.Remote, store.ClientSettings.Mode);
        Assert.Equal(ClientMode.Undefined, store.StartupSettings.Mode);
    }
}

public class NullUpdateServiceTests
{
    [Fact]
    public async Task Never_finds_an_update()
    {
        var service = new NullUpdateService();

        var info = await service.CheckForUpdatesAsync(CancellationToken.None);

        Assert.False(info.IsUpdateAvailable);
        Assert.Equal(info.CurrentVersion, info.TargetVersion);
        Assert.Null(info.ReleaseNotes);
    }

    [Fact]
    public void Reports_that_updating_is_not_supported()
    {
        var service = new NullUpdateService();

        Assert.False(service.IsUpdateSupported);
        Assert.Equal(UpdateChannel.None, service.Channel);
    }

    [Fact]
    public async Task Applying_an_update_does_nothing()
    {
        var service = new NullUpdateService();

        await service.DownloadAndApplyUpdateAsync(
            new AppUpdateInfo(true, "1.0.0", "1.0.1", null, null), progressCallback: null, CancellationToken.None);
        await service.OpenStoreListingAsync();
    }
}
