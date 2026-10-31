using OctaGrid.Client.Models;

namespace OctaGrid.Client.Services;

internal sealed class DesignSettingsStore : IAppSettingsStore
{
    public event EventHandler<ClientSettings>? SettingsChanged;

    public ClientSettings ClientSettings { get; private set; } = new();

    public ClientSettings StartupSettings { get; } = new();

    public void Update(Func<ClientSettings, ClientSettings> update)
    {
        var updated = update(ClientSettings);
        if (updated == ClientSettings)
        {
            return;
        }

        ClientSettings = updated;
        SettingsChanged?.Invoke(this, ClientSettings);
    }
}
