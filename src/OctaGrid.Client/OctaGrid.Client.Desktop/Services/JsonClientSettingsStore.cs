using System.Text.Json;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;

namespace OctaGrid.Client.Desktop.Services;

public class JsonClientSettingsStore : IAppSettingsStore
{
    private const string SettingFileName = "octaGrid.config.json";
    private readonly string _settingsFilePath;
    private ClientSettings? _clientSettings;
    private bool _settingsIsLoaded;
    private ClientSettings? _startupSettings;

    public JsonClientSettingsStore()
    {
        _settingsFilePath = Path.Combine(Helper.LocalDataFolder(), SettingFileName);
    }

    public event EventHandler<ClientSettings>? SettingsChanged;

    public ClientSettings ClientSettings
    {
        get
        {
            if (!_settingsIsLoaded)
            {
                Load();
            }
            return _clientSettings!;
        }
    }

    public ClientSettings StartupSettings
    {
        get
        {
            if (!_settingsIsLoaded)
            {
                Load();
            }
            return _startupSettings!;
        }
    }

    public void Update(Func<ClientSettings, ClientSettings> update)
    {
        var current = ClientSettings;
        var updated = update(current);
        if (updated == current)
        {
            return;
        }

        _clientSettings = updated;
        using (var fs = new FileStream(_settingsFilePath, FileMode.Create, FileAccess.Write))
        {
            JsonSerializer.Serialize(fs, _clientSettings, JsonSerializerOptions.Web);
        }

        SettingsChanged?.Invoke(this, _clientSettings);
    }

    private void Load()
    {
        using var fs = new FileStream(_settingsFilePath, FileMode.OpenOrCreate, FileAccess.Read);
        ClientSettings? response = null;
        try
        {
            response = JsonSerializer.Deserialize<ClientSettings>(fs, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
        }
        _settingsIsLoaded = true;
        _clientSettings = response is null ? new ClientSettings() : response;
        _startupSettings = _clientSettings;
    }
}
