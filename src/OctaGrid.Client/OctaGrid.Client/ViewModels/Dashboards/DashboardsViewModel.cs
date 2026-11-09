using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;
using OctaGrid.Protocol.Dashboards;
using OctaGrid.Protocol.Messaging;
using OctaGrid.Protocol.Printers;

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
    private readonly IAppSettingsStore _settings;
    private List<PrinterDashboardCardViewModel> _allPrinters = [];
    private bool _isUpdatingFilters;

    public DashboardsViewModel(
        IDashboardsClient dashboardsClient,
        IPrintersClient printersClient,
        ApplicationState appState,
        IAgentCommandsService agentCommands,
        IByteArrayImageCache imageCache,
        IAppSettingsStore settings,
        INotificationService? notifications = null,
        ILoggerFactory? loggerFactory = null)
    {
        _settings = settings;
        var savedStatus = settings.ClientSettings.DashboardStatusFilter;
        StatusFilters = Enum.GetValues<UniversalPrinterState>()
            .Select(state => new PrinterStatusFilterViewModel(state) { IsSelected = state == savedStatus })
            .ToList();
        foreach (var filter in StatusFilters)
        {
            filter.PropertyChanged += OnStatusFilterChanged;
        }

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
            foreach (var old in _allPrinters)
            {
                old.Item.PropertyChanged -= OnPrinterPropertyChanged;
            }

            var availableConnectors = _agentCommands.Connectors.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _allPrinters = printers.Select(printer => new PrinterDashboardCardViewModel(new PrinterStatusWidget()
            {
                Id = printer.Id,
                Name = printer.Name,
                ConnectorId = printer.ConnectorId,
                IsConnectorAvailable = availableConnectors.Contains(printer.ConnectorId),
            }, _agentCommands, _imageCache, _loggerFactory)).ToList();
            foreach (var card in _allPrinters)
            {
                card.Item.PropertyChanged += OnPrinterPropertyChanged;
            }

            BuildConnectorOptions();
            ApplyFilter();
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

    /// <summary>One selectable badge per printer state, with the number of printers in it.</summary>
    public IReadOnlyList<PrinterStatusFilterViewModel> StatusFilters { get; }

    /// <summary>The connector choices: all printers, then each connector that has a printer on the dashboard.</summary>
    [ObservableProperty]
    public partial ObservableCollection<ConnectorFilterOption> ConnectorOptions { get; set; } = [ConnectorFilterOption.All];

    [ObservableProperty]
    public partial ConnectorFilterOption SelectedConnector { get; set; } = ConnectorFilterOption.All;

    /// <summary>True when printers exist but the filter hides all of them.</summary>
    [ObservableProperty]
    public partial bool IsFilterHidingAll { get; set; }

    partial void OnSelectedConnectorChanged(ConnectorFilterOption value)
    {
        // The ComboBox writes null back while its ItemsSource is being replaced.
        if (_isUpdatingFilters || value is null)
        {
            return;
        }

        ApplyFilter();
        _settings.Update(s => s with { DashboardConnectorFilter = value.ConnectorId });
    }

    private void OnStatusFilterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isUpdatingFilters || e.PropertyName != nameof(PrinterStatusFilterViewModel.IsSelected)
            || sender is not PrinterStatusFilterViewModel changed)
        {
            return;
        }

        // Only one status can be selected at a time; clicking the selected badge again clears the filter.
        _isUpdatingFilters = true;
        try
        {
            if (changed.IsSelected)
            {
                foreach (var other in StatusFilters.Where(f => f != changed))
                {
                    other.IsSelected = false;
                }
            }
        }
        finally
        {
            _isUpdatingFilters = false;
        }

        ApplyFilter();
        _settings.Update(s => s with { DashboardStatusFilter = SelectedStatus });
    }

    private UniversalPrinterState? SelectedStatus => StatusFilters.FirstOrDefault(f => f.IsSelected)?.State;

    private void OnPrinterPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PrinterStatusWidget.State) or nameof(PrinterStatusWidget.IsConnectorAvailable))
        {
            ApplyFilter();
        }
    }

    private void BuildConnectorOptions()
    {
        var names = _agentCommands.Connectors.ToDictionary(c => c.Id, c => c.DisplayName);
        var options = new List<ConnectorFilterOption> { ConnectorFilterOption.All };
        options.AddRange(_allPrinters
            .Select(p => p.Item.ConnectorId)
            .Distinct()
            .Select(id => new ConnectorFilterOption(id, names.GetValueOrDefault(id, id)))
            .OrderBy(o => o.DisplayName, StringComparer.CurrentCultureIgnoreCase));

        // A saved connector that has no printers here is treated as "All" without forgetting the saved choice.
        var saved = _settings.ClientSettings.DashboardConnectorFilter;
        _isUpdatingFilters = true;
        try
        {
            ConnectorOptions = new ObservableCollection<ConnectorFilterOption>(options);
            SelectedConnector = options.FirstOrDefault(o => o.ConnectorId == saved) ?? ConnectorFilterOption.All;
        }
        finally
        {
            _isUpdatingFilters = false;
        }
    }

    private void ApplyFilter()
    {
        var byConnector = _allPrinters
            .Where(p => SelectedConnector?.ConnectorId is null || p.Item.ConnectorId == SelectedConnector.ConnectorId)
            .ToList();

        // A printer whose connector is unavailable never reports, so it must not keep the empty badges on screen.
        var statesKnown = _allPrinters.All(p => p.Item.IsStateKnown || !p.Item.IsConnectorAvailable);
        foreach (var filter in StatusFilters)
        {
            filter.Count = byConnector.Count(p => p.Item.IsStateKnown && p.Item.State == filter.State);
            filter.HideWhenEmpty = statesKnown;
        }

        // Printers that have not reported yet stay visible, so a restored filter does not blank the page at startup.
        var status = SelectedStatus;
        var visible = byConnector.Where(p => status is null || !p.Item.IsStateKnown || p.Item.State == status).ToList();
        // Replacing the collection re-creates every card view, so only do it when membership changed.
        if (!visible.SequenceEqual(Printers))
        {
            Printers = new ObservableCollection<PrinterDashboardCardViewModel>(visible);
        }

        IsFilterHidingAll = _allPrinters.Count > 0 && visible.Count == 0;
    }

    private bool IsDashboardSelected() => SelectedDashboard is not null;

    [RelayCommand(CanExecute = nameof(IsDashboardSelected))]
    private Task RefreshPrinters(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Refreshing printers triggered");
        return LoadData(cancellationToken);
    }
}
