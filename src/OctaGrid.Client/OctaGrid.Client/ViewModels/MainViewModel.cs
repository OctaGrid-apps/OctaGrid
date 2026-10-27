using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;

namespace OctaGrid.Client.ViewModels;

public partial class MainViewModel : ViewModelBase, IDialogParticipant
{
    private readonly IAgentsClient _agentsClient;
    private readonly IAppSettingsStore _appSettings;
    private readonly IWorkspacesClient _workspacesClient;

    // Design-time only: MainView.axaml's <Design.DataContext> instantiates this directly via
    // <vm:MainViewModel/>, which needs a public parameterless constructor. Never used at
    // runtime — App.axaml.cs's Initialize() populates App.Services with design-time mocks
    // (DesignOctaGridClient etc.) before the previewer can reach this.
    public MainViewModel() : this(
        App.Services.GetRequiredService<IWorkspacesClient>(),
        App.Services.GetRequiredService<IAgentsClient>(),
        App.Services.GetRequiredService<IAppSettingsStore>(),
        App.Services.GetRequiredService<ApplicationState>())
    {
    }

    public MainViewModel(
        IWorkspacesClient workspacesClient,
        IAgentsClient agentsClient,
        IAppSettingsStore appSettings,
        ApplicationState appState)
    {
        _workspacesClient = workspacesClient;
        _agentsClient = agentsClient;
        _appSettings = appSettings;
        AppStateService = appState;

        WeakReferenceMessenger.Default.Register<WorkspaceEntityChanged>(this, HandleWorkspaceChanged);
    }

    [ObservableProperty]
    public partial ApplicationState AppStateService { get; set; }

    public bool IsRemoteMode => _appSettings.StartupSettings.Mode == ClientMode.Remote;

    [ObservableProperty]
    public partial ObservableCollection<Workspace> Workspaces { get; set; } = [];

    private async Task CreateDefaultEntities(CancellationToken cancellationToken)
    {
        const string DefaultWorkspaceName = "Default";
        const string DefaultAgentName = "Local";

        var workspaceResponse = await _workspacesClient.CreateWorkspace(DefaultWorkspaceName, cancellationToken);
        var ws = new Workspace()
        {
            Id = workspaceResponse.Id,
            Name = workspaceResponse.Name
        };
        Workspaces.Add(ws);
        AppStateService.SelectedWorkspace = ws;

        _ = await _agentsClient.CreateAgent(workspaceResponse.Id, DefaultAgentName, cancellationToken);
    }

    private async void HandleWorkspaceChanged(object recipient, WorkspaceEntityChanged message)
    {
        await LoadData(CancellationToken.None);
    }

    [RelayCommand]
    private async Task LoadData(CancellationToken cancellationToken)
    {
        var response = await _workspacesClient.GetWorkspaceList(cancellationToken);
        Workspaces = new ObservableCollection<Workspace>(response.Select(w => new Workspace()
        {
            Id = w.Id,
            Name = w.Name
        }));

        if (Workspaces.Count == 1)
        {
            AppStateService.SelectedWorkspace = Workspaces.First();
            return;
        }

        if (Workspaces.Count > 1)
        {
            var defaultWorkspaceId = _appSettings.ClientSettings.DefaultWorkspace;
            if (defaultWorkspaceId.HasValue)
            {
                var workspace = Workspaces.FirstOrDefault(w => w.Id == defaultWorkspaceId);
                AppStateService.SelectedWorkspace = workspace ?? Workspaces.First();
            }
            else
            {
                AppStateService.SelectedWorkspace = Workspaces.First();
            }
            return;
        }

        await CreateDefaultEntities(cancellationToken);
    }
}
