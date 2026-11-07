using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;
using OctaGrid.Protocol.Printers;
using OctaGrid.Protocol.Workspaces;

namespace OctaGrid.Client.ViewModels;

public partial class DashboardsViewModel : ViewModelBase, IDialogParticipant
{
    /// <summary>The kind of dashboard created when a workspace has none yet.</summary>
    public const DashboardType DefaultType = DashboardType.List;

    private readonly IAgentCommandsService _agentCommands;
    private readonly ApplicationState _appState;
    private readonly IDashboardsClient _dashboardsClient;
    private readonly IByteArrayImageCache _imageCache;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<DashboardsViewModel> _logger;
    private readonly INotificationService? _notifications;
    private readonly IPrintersClient _printersClient;

    public DashboardsViewModel(
        IDashboardsClient dashboardsClient,
        IPrintersClient printersClient,
        ApplicationState appState,
        IAgentCommandsService agentCommands,
        IByteArrayImageCache imageCache,
        INotificationService? notifications = null,
        ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _agentCommands = agentCommands;
        _imageCache = imageCache;
        var logger = _loggerFactory.CreateLogger<DashboardsViewModel>();
        _dashboardsClient = dashboardsClient;
        _printersClient = printersClient;
        _notifications = notifications;
        _appState = appState;
        _logger = logger;
        _appState.PropertyChanged += async (s, e) =>
        {
            if (e.PropertyName == nameof(ApplicationState.SelectedWorkspace) && _appState.SelectedWorkspace is not null)
            {
                await LoadData(CancellationToken.None);
            }
        };
        _logger.LogDebug("DashboardsViewModel initialized");
    }

    [ObservableProperty]
    public partial ObservableCollection<DashboardResponse> Dashboards { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<PrinterDashboardCardViewModel> Printers { get; set; } = [];

    [ObservableProperty]
    public partial DashboardResponse? SelectedDashboard { get; set; }

    /// <summary>Opens the dialog that creates, renames and deletes dashboards and picks the default one.</summary>
    [RelayCommand]
    private async Task ManageDashboards(CancellationToken cancellationToken)
    {
        if (_appState.SelectedWorkspace is null)
        {
            return;
        }

        _logger.LogInformation("Opening the dashboard management dialog");
        var workspaceId = _appState.GetWorkspaceId();
        var viewModel = new ManageDashboardsViewModel(
            _dashboardsClient,
            workspaceId,
            Dashboards,
            _notifications,
            _loggerFactory.CreateLogger<ManageDashboardsViewModel>());

        var changed = await this.ShowOverlayDialogAsync("Manage dashboards", viewModel);
        if (changed == true)
        {
            await LoadData(cancellationToken);
        }
    }

    [RelayCommand]
    private async Task LoadData(CancellationToken cancellationToken)
    {
        if (_appState.SelectedWorkspace is null)
        {
            _logger.LogWarning("LoadData skipped: No workspace selected");
            return;
        }

        var workspaceId = _appState.GetWorkspaceId();
        _logger.LogInformation("Loading dashboard data for workspace {WorkspaceId}", workspaceId);

        try
        {
            // Remember the open dashboard before the list is replaced, which can clear the selection.
            var openDashboardId = SelectedDashboard?.Id;

            var response = await _dashboardsClient.GetDashboardList(workspaceId, cancellationToken);
            _logger.LogInformation("Loaded {Count} dashboards for workspace {WorkspaceId}", response.Count, workspaceId);

            if (response.Count == 0)
            {
                _logger.LogInformation("Workspace {WorkspaceId} has no dashboards; provisioning default dashboard", workspaceId);
                const string DefaultDashboardName = "Default";
                var defaultDashboard = await _dashboardsClient.CreateDashboard(
                    workspaceId,
                    new CreateDashboardRequest(DefaultDashboardName, DefaultType),
                    cancellationToken);
                response = [defaultDashboard];
            }

            Dashboards = new ObservableCollection<DashboardResponse>(response);

            // Stay on the open dashboard; if it is gone (deleted), fall back to the workspace default.
            var next = response.FirstOrDefault(d => d.Id == openDashboardId)
                ?? response.FirstOrDefault(d => d.IsDefault)
                ?? response[0];
            if (next.Id != openDashboardId)
            {
                _logger.LogInformation("Selecting dashboard: {DashboardName} ({DashboardId})", next.Name, next.Id);
            }

            SelectedDashboard = next;

            _logger.LogDebug("Loading enabled printers for workspace {WorkspaceId}", workspaceId);
            var printers = await _printersClient.GetPrintersList(workspaceId, new GetPrintersQuery(Enabled: true), cancellationToken);
            Printers = new ObservableCollection<PrinterDashboardCardViewModel>(printers.Select(printer => new PrinterDashboardCardViewModel(new PrinterStatusWidget()
            {
                Id = printer.Id,
                Name = printer.Name
            }, _agentCommands, _imageCache, _loggerFactory)));
            _logger.LogInformation("Loaded {Count} printers for workspace {WorkspaceId}", printers.Count, workspaceId);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Loading dashboard data was aborted via cancellation token");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load dashboard data for workspace {WorkspaceId}", workspaceId);
            throw;
        }
    }

    partial void OnSelectedDashboardChanged(DashboardResponse? oldValue, DashboardResponse? newValue)
    {
        _logger.LogDebug("Selected dashboard changed: {OldDashboard} -> {NewDashboard}", oldValue?.Name ?? "None", newValue?.Name ?? "None");
        RefreshPrintersCommand.NotifyCanExecuteChanged();
    }

    private bool IsDashboardSelected() => SelectedDashboard is not null;

    [RelayCommand(CanExecute = nameof(IsDashboardSelected))]
    private Task RefreshPrinters(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Refreshing printers triggered");
        return LoadData(cancellationToken);
    }
}
