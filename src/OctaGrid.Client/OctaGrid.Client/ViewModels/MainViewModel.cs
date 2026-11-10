using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;
using OctaGrid.Protocol.Dashboards;

namespace OctaGrid.Client.ViewModels;

public partial class MainViewModel : ViewModelBase, IDialogParticipant
{
    private readonly IAgentsClient _agentsClient;
    private readonly IAppSettingsStore _appSettings;
    private readonly IDashboardsClient _dashboardsClient;
    private readonly ILogger<MainViewModel> _logger;
    private readonly INotificationService _notifications;
    private readonly IUpdateService _updateService;
    private readonly IWorkspacesClient _workspacesClient;
    private bool _hasCheckedForUpdates;

    public MainViewModel(
        IWorkspacesClient workspacesClient,
        IAgentsClient agentsClient,
        IDashboardsClient dashboardsClient,
        IUpdateService updateService,
        IAppSettingsStore appSettings,
        ApplicationState appState,
        INotificationService notifications,
        DashboardsViewModel dashboardsPage,
        PrinterListViewModel printersPage,
        AgentListViewModel agentsPage,
        WorkspaceListViewModel workspacesPage,
        SettingsViewModel settingsPage,
        ILogger<MainViewModel>? logger = null)
    {
        _notifications = notifications;
        DashboardsPage = dashboardsPage;
        PrintersPage = printersPage;
        AgentsPage = agentsPage;
        WorkspacesPage = workspacesPage;
        SettingsPage = settingsPage;
        _workspacesClient = workspacesClient;
        _agentsClient = agentsClient;
        _dashboardsClient = dashboardsClient;
        _updateService = updateService;
        _appSettings = appSettings;
        AppStateService = appState;
        _logger = logger ?? NullLogger<MainViewModel>.Instance;

        WeakReferenceMessenger.Default.Register<WorkspaceEntityChanged>(this, HandleWorkspaceChanged);
        WeakReferenceMessenger.Default.Register<OpenPrinterPageRequested>(this, (_, message) =>
            PrinterPage = new PrinterPageViewModel(message.Card, () => PrinterPage = null, _appSettings, _notifications));

        AppStateService.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ApplicationState.IsUpdateAvailable))
            {
                IsUpdateAvailable = AppStateService.IsUpdateAvailable;
            }
        };
    }

    /// <summary>The page shown under "Agents" (Remote mode).</summary>
    public AgentListViewModel AgentsPage { get; }

    [ObservableProperty]
    public partial ApplicationState AppStateService { get; set; }

    /// <summary>The page shown under "Dashboard"; given to the navigation item as its data context.</summary>
    public DashboardsViewModel DashboardsPage { get; }

    public bool IsRemoteMode => _appSettings.StartupSettings.Mode == ClientMode.Remote;

    [ObservableProperty]
    public partial bool IsUpdateAvailable { get; set; }

    /// <summary>
    /// The printer page opened on top of the current navigation page, or null. Choosing an item in the rail
    /// clears it, and the page's back button does too, so the page the user came from is shown again unchanged.
    /// </summary>
    [ObservableProperty]
    public partial PrinterPageViewModel? PrinterPage { get; set; }

    // The page closes by its back button or by choosing something in the rail; either way it must stop listening.
    partial void OnPrinterPageChanged(PrinterPageViewModel? oldValue, PrinterPageViewModel? newValue) => oldValue?.Stop();

    /// <summary>The page shown under "Printers".</summary>
    public PrinterListViewModel PrintersPage { get; }

    /// <summary>The page shown under "Settings".</summary>
    public SettingsViewModel SettingsPage { get; }

    [ObservableProperty]
    public partial ObservableCollection<Workspace> Workspaces { get; set; } = [];

    /// <summary>The page shown under "Workspaces" (Remote mode).</summary>
    public WorkspaceListViewModel WorkspacesPage { get; }

    /// <summary>Lets notifications appear in the window once the view has one.</summary>
    public void AttachNotifications(Avalonia.Controls.TopLevel topLevel) => _notifications.Attach(topLevel);

    private async Task CheckForUpdatesOnStartupAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Checking for updates on app startup...");
            var updateInfo = await _updateService.CheckForUpdatesAsync(cancellationToken);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                AppStateService.AvailableUpdate = updateInfo;
                IsUpdateAvailable = updateInfo.IsUpdateAvailable;
            });

            if (updateInfo.IsUpdateAvailable)
            {
                _logger.LogInformation("Startup update check: new version {TargetVersion} is available", updateInfo.TargetVersion);
            }
            else
            {
                _logger.LogDebug("Startup update check: application is up to date ({CurrentVersion})", updateInfo.CurrentVersion);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Startup update check was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Background startup update check encountered an error");
        }
    }

    private async Task CreateDefaultEntities(CancellationToken cancellationToken)
    {
        const string DefaultWorkspaceName = "Default";
        const string DefaultAgentName = "Local";
        const string DefaultDashboardName = "Default";

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
        if (dashboards.Count == 0)
        {
            _ = await _dashboardsClient.CreateDashboard(
                workspaceResponse.Id,
                new CreateDashboardRequest(DefaultDashboardName, DashboardsViewModel.DefaultType),
                cancellationToken);
        }
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

            // The designer has no overlay layer to show the dialog in, and nobody to answer it.
            if (!Avalonia.Controls.Design.IsDesignMode && !_appSettings.ClientSettings.IsSafetyDisclaimerAccepted)
            {
                _logger.LogInformation("Safety disclaimer has not been accepted yet; displaying SafetyDisclaimerView");
                var disclaimerViewModel = new SafetyDisclaimerViewModel(null);
                var accepted = await this.ShowOverlayDialogAsync("Safety Warning & Disclaimer", disclaimerViewModel);
                if (accepted)
                {
                    _appSettings.Update(s => s with { IsSafetyDisclaimerAccepted = true });
                }
            }

            if (!_hasCheckedForUpdates)
            {
                _hasCheckedForUpdates = true;
                if (_updateService.CheckOnStartup)
                {
                    _ = CheckForUpdatesOnStartupAsync(cancellationToken);
                }
                else
                {
                    _logger.LogDebug("Skipping the startup update check for this build");
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
