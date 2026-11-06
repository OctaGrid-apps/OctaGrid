using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;

namespace OctaGrid.Client.Desktop.Services;

/// <summary>Keeps the client settings in a JSON file, loaded on first use.</summary>
public class JsonClientSettingsStore : InMemorySettingsStore
{
    private readonly ILogger<JsonClientSettingsStore> _logger;
    private readonly string _settingsFilePath;
    private bool _settingsIsLoaded;

    /// <param name="logger">Optional logger.</param>
    /// <param name="settingsFilePath">The settings file; defaults to the one in the OctaGrid data folder.</param>
    public JsonClientSettingsStore(ILogger<JsonClientSettingsStore>? logger = null, string? settingsFilePath = null)
    {
        _logger = logger ?? NullLogger<JsonClientSettingsStore>.Instance;
        _settingsFilePath = settingsFilePath ?? OctaGridDataPaths.SettingsPath;
    }

    public override ClientSettings ClientSettings
    {
        get
        {
            EnsureLoaded();
            return base.ClientSettings;
        }
    }

    public override ClientSettings StartupSettings
    {
        get
        {
            EnsureLoaded();
            return base.StartupSettings;
        }
    }

    protected override void Persist(ClientSettings settings)
    {
        using (var fs = new FileStream(_settingsFilePath, FileMode.Create, FileAccess.Write))
        {
            JsonSerializer.Serialize(fs, settings, JsonSerializerOptions.Web);
        }

        _logger.LogInformation("Saved updated client settings to {SettingsPath}", _settingsFilePath);
    }

    private void EnsureLoaded()
    {
        if (!_settingsIsLoaded)
        {
            Load();
        }
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
        Initialize(response ?? new ClientSettings());
    }
}
