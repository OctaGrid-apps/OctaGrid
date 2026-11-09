using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Services;

namespace OctaGrid.Client.Models;

public partial class ApplicationState : ObservableObject
{
    private readonly ILogger<ApplicationState> _logger;
    private readonly IAppSettingsStore _settingsStore;

    public ApplicationState(IAppSettingsStore settingsStore, ILogger<ApplicationState>? logger = null)
    {
        _settingsStore = settingsStore;
        _logger = logger ?? NullLogger<ApplicationState>.Instance;
    }

    public ClientMode ActiveClientMode => _settingsStore.StartupSettings.Mode;

    [ObservableProperty]
    public partial Workspace? SelectedWorkspace { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUpdateAvailable))]
    public partial AppUpdateInfo? AvailableUpdate { get; set; }

    public bool IsUpdateAvailable => AvailableUpdate is { IsUpdateAvailable: true };

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
            _logger.LogInformation("Active workspace changed to '{WorkspaceName}' ({WorkspaceId})", value.Name, value.Id);
            _settingsStore.Update(s => s with { DefaultWorkspace = value.Id });
        }
    }
}
