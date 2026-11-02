using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly IAppSettingsStore _appSettingsStore;
    private readonly ApplicationState _appState;
    private readonly IDashboardsClient _dashboardsClient;
    private readonly ILogger<DashboardsViewModel> _logger;
    private readonly IPrintersClient _printersClient;

    // Design-time only — see MainViewModel's parameterless constructor for why this exists.
    public DashboardsViewModel() : this(
        App.Services.GetRequiredService<IDashboardsClient>(),
        App.Services.GetRequiredService<IPrintersClient>(),
        App.Services.GetRequiredService<IAppSettingsStore>(),
        App.Services.GetRequiredService<ApplicationState>(),
        App.Services.GetService<ILogger<DashboardsViewModel>>() ?? NullLogger<DashboardsViewModel>.Instance)
    {
    }

    public DashboardsViewModel(
        IDashboardsClient dashboardsClient,
        IPrintersClient printersClient,
        IAppSettingsStore appSettingsStore,
        ApplicationState appState,
        ILogger<DashboardsViewModel>? logger = null)
    {
        _dashboardsClient = dashboardsClient;
        _printersClient = printersClient;
        _appSettingsStore = appSettingsStore;
        _appState = appState;
        _logger = logger ?? NullLogger<DashboardsViewModel>.Instance;
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

    [RelayCommand]
    private async Task CreateDashboard(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Opening Create Dashboard dialog");
        var dashboardViewModel = new EditDashboardViewModel();

        // Show the dialog and wait for the result.
        var result = await this.ShowOverlayDialogAsync("Create new dashboard", dashboardViewModel);
        if (result is null)
        {
            _logger.LogInformation("Create dashboard flow cancelled (no dashboard created)");
            return;
        }

        const string DashboardType = "List";
        var workspaceId = _appState.GetWorkspaceId();
        _logger.LogInformation("Creating dashboard '{DashboardName}' (type: {DashboardType}) in workspace {WorkspaceId}", result, DashboardType, workspaceId);

        try
        {
            _ = await _dashboardsClient.CreateDashboard(workspaceId, new CreateDashboardRequest(result, DashboardType), cancellationToken);
            _logger.LogInformation("Dashboard '{DashboardName}' created successfully", result);
            await LoadData(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Dashboard creation was aborted via cancellation token");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create dashboard '{DashboardName}' in workspace {WorkspaceId}", result, workspaceId);
            throw;
        }
    }

    [RelayCommand(CanExecute = nameof(IsDashboardSelected))]
    private async Task EditSelectedDashboard(CancellationToken cancellationToken)
    {
        if (SelectedDashboard is null)
        {
            return;
        }

        var printerViewModel = new EditDashboardViewModel(new DashboardViewModel(SelectedDashboard.Name), App.Services.GetRequiredService<ILogger<EditDashboardViewModel>>());

        var result = await this.ShowOverlayDialogAsync("Edit dashboard", printerViewModel);
        if (result is null)
        {
            return;
        }

        try
        {
            _logger.LogInformation("Updating dashboard {DashboardId} '{DashboardNameName}'",
                SelectedDashboard.Id, SelectedDashboard.Name);
            var response = await _dashboardsClient.UpdateDashboard(SelectedDashboard.Id, new UpdateDashboardRequest(result), cancellationToken);
            await LoadData(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to update dashboard {DashboardId}", SelectedDashboard.Id);
            throw;
        }
    }

    private bool IsDashboardSelected() => SelectedDashboard is not null;

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
            var response = await _dashboardsClient.GetDashboardList(workspaceId, cancellationToken);
            Dashboards = new ObservableCollection<DashboardResponse>(response);
            _logger.LogInformation("Loaded {Count} dashboards for workspace {WorkspaceId}", response.Count, workspaceId);

            if (response.Count == 0)
            {
                _logger.LogInformation("Workspace {WorkspaceId} has no dashboards; provisioning default dashboard", workspaceId);
                const string DefaultDashboardName = "Default";
                const string DashboardType = "List";
                var defaultDashboard = await _dashboardsClient.CreateDashboard(
                    workspaceId,
                    new CreateDashboardRequest(DefaultDashboardName, DashboardType),
                    cancellationToken);

                Dashboards = new ObservableCollection<DashboardResponse>([defaultDashboard]);
                SelectNewDashboard(defaultDashboard);
                response = [defaultDashboard];
            }

            if (SelectedDashboard is null)
            {
                var defaultDashoardId = _appSettingsStore.ClientSettings.DefaultDashboard;
                if (defaultDashoardId is not null && response.Any(d => d.Id == defaultDashoardId))
                {
                    SelectedDashboard = response.First(d => d.Id == defaultDashoardId);
                    _logger.LogDebug("Restored default dashboard: {DashboardName} ({DashboardId})", SelectedDashboard.Name, SelectedDashboard.Id);
                }
                else
                {
                    SelectNewDashboard(response.First());
                }
            }
            else
            {
                if (!response.Any(d => d.Id == SelectedDashboard.Id))
                {
                    _logger.LogInformation("Previously selected dashboard ({DashboardId}) no longer exists; selecting first available", SelectedDashboard.Id);
                    SelectNewDashboard(response.First());
                }
            }

            _logger.LogDebug("Loading enabled printers for workspace {WorkspaceId}", workspaceId);
            var printers = await _printersClient.GetPrintersList(workspaceId, new GetPrintersQuery(Enabled: true), cancellationToken);
            Printers = new ObservableCollection<PrinterDashboardCardViewModel>(printers.Select(printer => new PrinterDashboardCardViewModel(new PrinterStatusWidget()
            {
                Id = printer.Id,
                Name = printer.Name
            })));
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
        EditSelectedDashboardCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(IsDashboardSelected))]
    private Task RefreshPrinters(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Refreshing printers triggered");
        return LoadData(cancellationToken);
    }

    private void SelectNewDashboard(DashboardResponse dashboard)
    {
        _logger.LogInformation("Selecting dashboard: {DashboardName} ({DashboardId})", dashboard.Name, dashboard.Id);
        SelectedDashboard = dashboard;
        _appSettingsStore.Update(s => s with { DefaultDashboard = dashboard.Id });
    }
}
