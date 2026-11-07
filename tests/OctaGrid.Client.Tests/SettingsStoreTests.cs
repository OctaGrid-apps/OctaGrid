using OctaGrid.Client.Models;
using OctaGrid.Client.Services;

namespace OctaGrid.Client.Tests;

public class SettingsStoreTests
{
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
