using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;

namespace OctaGrid.Client.Desktop.Services;

public class JsonClientSettingsStore : IAppSettingsStore
{
    private readonly ILogger<JsonClientSettingsStore> _logger;
    private readonly string _settingsFilePath;
    private ClientSettings? _clientSettings;
    private bool _settingsIsLoaded;
    private ClientSettings? _startupSettings;

    public JsonClientSettingsStore(ILogger<JsonClientSettingsStore>? logger = null)
    {
        _logger = logger ?? NullLogger<JsonClientSettingsStore>.Instance;
        _settingsFilePath = OctaGridDataPaths.SettingsPath;
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

        _logger.LogInformation("Saved updated client settings to {SettingsPath}", _settingsFilePath);
        SettingsChanged?.Invoke(this, _clientSettings);
    }

    private void Load()
    {
        using var fs = new FileStream(_settingsFilePath, FileMode.OpenOrCreate, FileAccess.Read);
        ClientSettings? response = null;
        if (fs.Length > 0)
        {
            try
            {
                response = JsonSerializer.Deserialize<ClientSettings>(fs, JsonSerializerOptions.Web);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize client settings from {SettingsPath}, using defaults", _settingsFilePath);
            }
        }
        _settingsIsLoaded = true;
        _clientSettings = response is null ? new ClientSettings() : response;
        _startupSettings = _clientSettings;
    }
}
