using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;
using OctaGrid.Protocol.Workspaces;

namespace OctaGrid.Client.ViewModels;

public partial class MainViewModel : ViewModelBase, IDialogParticipant
{
    private readonly IAgentsClient _agentsClient;
    private readonly IAppSettingsStore _appSettings;
    private readonly IDashboardsClient _dashboardsClient;
    private readonly ILogger<MainViewModel> _logger;
    private readonly IWorkspacesClient _workspacesClient;

    // Design-time only: MainView.axaml's <Design.DataContext> instantiates this directly via
    // <vm:MainViewModel/>, which needs a public parameterless constructor. Never used at
    // runtime — App.axaml.cs's Initialize() populates App.Services with design-time mocks
    // (DesignOctaGridClient etc.) before the previewer can reach this.
    public MainViewModel() : this(
        App.Services.GetRequiredService<IWorkspacesClient>(),
        App.Services.GetRequiredService<IAgentsClient>(),
        App.Services.GetRequiredService<IDashboardsClient>(),
        App.Services.GetRequiredService<IAppSettingsStore>(),
        App.Services.GetRequiredService<ApplicationState>(),
        App.Services.GetService<ILogger<MainViewModel>>() ?? NullLogger<MainViewModel>.Instance)
    {
    }

    public MainViewModel(
        IWorkspacesClient workspacesClient,
        IAgentsClient agentsClient,
        IDashboardsClient dashboardsClient,
        IAppSettingsStore appSettings,
        ApplicationState appState,
        ILogger<MainViewModel>? logger = null)
    {
        _workspacesClient = workspacesClient;
        _agentsClient = agentsClient;
        _dashboardsClient = dashboardsClient;
        _appSettings = appSettings;
        AppStateService = appState;
        _logger = logger ?? NullLogger<MainViewModel>.Instance;

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
        const string DefaultDashboardName = "Default";
        const string DefaultDashboardType = "List";

        _logger.LogInformation("No workspaces found; provisioning default workspace '{WorkspaceName}', agent '{AgentName}', and dashboard '{DashboardName}'",
            DefaultWorkspaceName, DefaultAgentName, DefaultDashboardName);

        var workspaceResponse = await _workspacesClient.CreateWorkspace(DefaultWorkspaceName, cancellationToken);
        var ws = new Workspace()
        {
            Id = workspaceResponse.Id,
            Name = workspaceResponse.Name
        };
        Workspaces.Add(ws);
        AppStateService.SelectedWorkspace = ws;

        _ = await _agentsClient.CreateAgent(workspaceResponse.Id, DefaultAgentName, cancellationToken);

        var dashboards = await _dashboardsClient.GetDashboardList(workspaceResponse.Id, cancellationToken);
        var defaultDashboard = dashboards.FirstOrDefault();
        if (defaultDashboard is null)
        {
            defaultDashboard = await _dashboardsClient.CreateDashboard(
                workspaceResponse.Id,
                new CreateDashboardRequest(DefaultDashboardName, DefaultDashboardType),
                cancellationToken);
        }

        _appSettings.Update(s => s with { DefaultDashboard = defaultDashboard.Id });
    }

    private async void HandleWorkspaceChanged(object recipient, WorkspaceEntityChanged message)
    {
        try
        {
            await LoadData(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload workspaces after WorkspaceEntityChanged ({WorkspaceId})", message.Value.EntityId);
        }
    }

    [RelayCommand]
    private async Task LoadData(CancellationToken cancellationToken)
    {
        try
        {
            await LoadWorkspacesAsync(cancellationToken);

            if (!_appSettings.ClientSettings.IsSafetyDisclaimerAccepted)
            {
                _logger.LogInformation("Safety disclaimer has not been accepted yet; displaying SafetyDisclaimerView");
                var disclaimerViewModel = new SafetyDisclaimerViewModel();
                var accepted = await this.ShowOverlayDialogAsync("Safety Warning & Disclaimer", disclaimerViewModel);
                if (accepted)
                {
                    _appSettings.Update(s => s with { IsSafetyDisclaimerAccepted = true });
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("MainViewModel LoadData aborted via cancellation token");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load workspaces in MainViewModel");
            throw;
        }
    }

    private async Task LoadWorkspacesAsync(CancellationToken cancellationToken)
    {
        var response = await _workspacesClient.GetWorkspaceList(cancellationToken);
        Workspaces = new ObservableCollection<Workspace>(response.Select(w => new Workspace()
        {
            Id = w.Id,
            Name = w.Name
        }));

        _logger.LogDebug("Loaded {WorkspaceCount} workspace(s)", Workspaces.Count);

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
