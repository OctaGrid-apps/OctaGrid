using Microsoft.Extensions.DependencyInjection;
using OctaGrid.Client.Desktop.Services;
using OctaGrid.Client.Models;

namespace OctaGrid.Client.Desktop.Tests;

public sealed class JsonClientSettingsStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "octagrid-tests", Guid.NewGuid().ToString("N"));

    public JsonClientSettingsStoreTests() => Directory.CreateDirectory(_folder);

    private string SettingsPath => Path.Combine(_folder, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private JsonClientSettingsStore NewStore() => new(settingsFilePath: SettingsPath);

    [Fact]
    public void A_new_file_gives_default_settings_and_is_created()
    {
        var store = NewStore();

        Assert.Equal(new ClientSettings(), store.ClientSettings);
        Assert.True(File.Exists(SettingsPath));
    }

    [Fact]
    public void Updates_are_saved_and_read_by_the_next_store()
    {
        NewStore().Update(s => s with { ElementTheme = ElementTheme.Dark, AskToSelectClientMode = true, Mode = ClientMode.Local });

        var next = NewStore();

        Assert.Equal(ElementTheme.Dark, next.ClientSettings.ElementTheme);
        Assert.True(next.ClientSettings.AskToSelectClientMode);
        Assert.Equal(ClientMode.Local, next.ClientSettings.Mode);
    }

    [Fact]
    public void Startup_settings_are_the_saved_ones_and_do_not_follow_later_updates()
    {
        NewStore().Update(s => s with { ElementTheme = ElementTheme.Light });
        var store = NewStore();

        store.Update(s => s with { ElementTheme = ElementTheme.Dark });

        Assert.Equal(ElementTheme.Light, store.StartupSettings.ElementTheme);
        Assert.Equal(ElementTheme.Dark, store.ClientSettings.ElementTheme);
    }

    [Fact]
    public void The_event_is_raised_after_the_file_is_saved()
    {
        var store = NewStore();
        string? textWhenRaised = null;
        store.SettingsChanged += (_, _) => textWhenRaised = File.ReadAllText(SettingsPath);

        store.Update(s => s with { AskToSelectClientMode = true });

        Assert.NotNull(textWhenRaised);
        Assert.Contains("askToSelectClientMode", textWhenRaised);
    }

    [Fact]
    public void An_update_that_changes_nothing_does_not_touch_the_file()
    {
        var store = NewStore();
        _ = store.ClientSettings;
        var before = File.GetLastWriteTimeUtc(SettingsPath);
        var raised = 0;
        store.SettingsChanged += (_, _) => raised++;

        store.Update(s => s);

        Assert.Equal(0, raised);
        Assert.Equal(before, File.GetLastWriteTimeUtc(SettingsPath));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{ \"mode\": ")]
    public void A_damaged_file_falls_back_to_default_settings(string content)
    {
        File.WriteAllText(SettingsPath, content);

        var store = NewStore();

        Assert.Equal(new ClientSettings(), store.ClientSettings);
    }

    [Fact]
    public void Settings_are_not_read_until_they_are_needed()
    {
        var store = NewStore();

        Assert.False(File.Exists(SettingsPath));

        _ = store.ClientSettings;

        Assert.True(File.Exists(SettingsPath));
    }

    [Fact]
    public void The_container_can_build_it_the_way_the_app_registers_it()
    {
        var services = new ServiceCollection();
        services.AddSingleton<OctaGrid.Client.Services.IAppSettingsStore, JsonClientSettingsStore>();
        using var provider = services.BuildServiceProvider();

        // Resolving must not read or create the real settings file; that happens on first use.
        var store = provider.GetRequiredService<OctaGrid.Client.Services.IAppSettingsStore>();

        Assert.IsType<JsonClientSettingsStore>(store);
    }
}
