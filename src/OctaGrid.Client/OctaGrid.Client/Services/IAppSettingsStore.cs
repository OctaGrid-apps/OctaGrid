using OctaGrid.Client.Models;

namespace OctaGrid.Client.Services;

public interface IAppSettingsStore
{
    event EventHandler<ClientSettings>? SettingsChanged;

    ClientSettings ClientSettings { get; }

    ClientSettings StartupSettings { get; }

    void Update(Func<ClientSettings, ClientSettings> update);
}
