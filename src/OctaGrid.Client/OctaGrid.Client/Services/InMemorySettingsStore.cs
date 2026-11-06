using OctaGrid.Client.Models;

namespace OctaGrid.Client.Services;

/// <summary>
/// Keeps the client settings in memory. It is the store used by the designer, by tests and by clients
/// that have no file system; <c>JsonClientSettingsStore</c> builds on it by loading and saving a file.
/// </summary>
public class InMemorySettingsStore : IAppSettingsStore
{
    private ClientSettings _clientSettings;
    private ClientSettings _startupSettings;

    public InMemorySettingsStore(ClientSettings? initial = null)
    {
        _clientSettings = initial ?? new ClientSettings();
        _startupSettings = _clientSettings;
    }

    public event EventHandler<ClientSettings>? SettingsChanged;

    /// <summary>The current settings.</summary>
    public virtual ClientSettings ClientSettings => _clientSettings;

    /// <summary>The settings as they were when the application started.</summary>
    public virtual ClientSettings StartupSettings => _startupSettings;

    /// <summary>Applies a change. Nothing happens (and no event is raised) when the result equals the current settings.</summary>
    public void Update(Func<ClientSettings, ClientSettings> update)
    {
        var current = ClientSettings;
        var updated = update(current);
        if (updated == current)
        {
            return;
        }

        _clientSettings = updated;
        Persist(updated);
        SettingsChanged?.Invoke(this, updated);
    }

    /// <summary>Called after a change was applied; stores that keep settings elsewhere save them here.</summary>
    protected virtual void Persist(ClientSettings settings)
    {
    }

    /// <summary>Sets both the current and the startup settings, for stores that load them from somewhere.</summary>
    protected void Initialize(ClientSettings settings)
    {
        _clientSettings = settings;
        _startupSettings = settings;
    }
}
