using CommunityToolkit.Mvvm.ComponentModel;
using OctaGrid.Client.Services;

namespace OctaGrid.Client.Models;

public partial class ApplicationState : ObservableObject
{
    private readonly IAppSettingsStore _settingsStore;

    public ApplicationState(IAppSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    public ClientMode ActiveClientMode => _settingsStore.StartupSettings.Mode;

    public TransparencyLevel ActiveTransparencyLevel => _settingsStore.StartupSettings.TransparencyLevel;

    [ObservableProperty]
    public partial Workspace? SelectedWorkspace { get; set; }

    public Guid GetWorkspaceId()
    {
        if (SelectedWorkspace?.Id is null)
        {
            throw new InvalidOperationException("No workspace selected.");
        }
        return SelectedWorkspace.Id;
    }

    partial void OnSelectedWorkspaceChanged(Workspace? value)
    {
        if (value is not null)
        {
            _settingsStore.Update(s => s with { DefaultWorkspace = value.Id });
        }
    }
}
