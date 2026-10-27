using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;
using OctaGrid.Protocol.Messaging;
using OctaGrid.Protocol.Printers;
using OctaGrid.Protocol.Workspaces;

namespace OctaGrid.Client.ViewModels;

public class PrinterStatusWidget : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public double BedTargetTemperature
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(BedTargetTemperature));
            }
        }
    }

    public double BedTemperature
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(BedTemperature));
            }
        }
    }

    // Completed/Cancelled/Failed are last-job results: the printer is ready, so a new print can start.
    public bool CanStartPause => State is UniversalPrinterState.Idle or UniversalPrinterState.Completed
                                   or UniversalPrinterState.Cancelled or UniversalPrinterState.Failed
                                   or UniversalPrinterState.Printing or UniversalPrinterState.Paused;

    public bool CanStop => State is UniversalPrinterState.Preparing or UniversalPrinterState.Printing
                                  or UniversalPrinterState.Paused;

    public int? CurrentLayer
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(CurrentLayer));
            }
        }
    }

    public Guid Id { get; set; }

    public bool IsPaused => State == UniversalPrinterState.Paused;

    public bool IsPrinting => State == UniversalPrinterState.Printing;

    public string Name
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(Name));
            }
        }
    } = string.Empty;

    public double NozzleTargetTemperature
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(NozzleTargetTemperature));
            }
        }
    }

    public double NozzleTemperature
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(NozzleTemperature));
            }
        }
    }

    public string? PrintFileName
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(PrintFileName));
            }
        }
    }

    public TimeOnly PrintTime
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(PrintTime));
                OnPropertyChanged(nameof(PrintTimeLeft));
            }
        }
    }

    public TimeOnly PrintTimeLeft => TimeOnly.FromTimeSpan(PrintTimeTotal - PrintTime);

    public TimeOnly PrintTimeTotal
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(PrintTimeTotal));
            }
        }
    }

    public double Progress
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(Progress));
            }
        }
    }

    public string StartPauseLabel => State switch
    {
        UniversalPrinterState.Printing => "Pause",
        UniversalPrinterState.Paused => "Resume",
        _ => "Start"
    };

    public UniversalPrinterState State
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(State));
                OnPropertyChanged(nameof(IsPrinting));
                OnPropertyChanged(nameof(IsPaused));
                OnPropertyChanged(nameof(CanStartPause));
                OnPropertyChanged(nameof(CanStop));
            }
        }
    }

    public string? StateMessage => StateMessages.FirstOrDefault();

    public ObservableCollection<string> StateMessages
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(StateMessages));
                OnPropertyChanged(nameof(StateMessage));
            }
        }
    } = [];

    public byte[] ThumbnailData
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged(nameof(ThumbnailData));
        }
    } = [];

    public int? TotalLayerCount
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(TotalLayerCount));
            }
        }
    }

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public partial class DashboardsViewModel : ViewModelBase, IDialogParticipant
{
    private readonly IAppSettingsStore _appSettingsStore;
    private readonly ApplicationState _appState;
    private readonly IDashboardsClient _dashboardsClient;
    private readonly IPrintersClient _printersClient;

    // Design-time only — see MainViewModel's parameterless constructor for why this exists.
    public DashboardsViewModel() : this(
        App.Services.GetRequiredService<IDashboardsClient>(),
        App.Services.GetRequiredService<IPrintersClient>(),
        App.Services.GetRequiredService<IAppSettingsStore>(),
        App.Services.GetRequiredService<ApplicationState>())
    {
    }

    public DashboardsViewModel(
        IDashboardsClient dashboardsClient,
        IPrintersClient printersClient,
        IAppSettingsStore appSettingsStore,
        ApplicationState appState)
    {
        _dashboardsClient = dashboardsClient;
        _printersClient = printersClient;
        _appSettingsStore = appSettingsStore;
        _appState = appState;
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
        var dashboardViewModel = new EditDashboardViewModel();

        // Show the dialog and wait for the result.
        var result = await this.ShowOverlayDialogAsync("Create new dashboard", dashboardViewModel);
        if (result is null)
        {
            return;
        }

        const string DashboardType = "List";
        var workspaceId = _appState.GetWorkspaceId();
        _ = await _dashboardsClient.CreateDashboard(workspaceId, new CreateDashboardRequest(result, DashboardType), cancellationToken);
        await LoadData(cancellationToken);
    }

    [RelayCommand]
    private async Task LoadData(CancellationToken cancellationToken)
    {
        if (_appState.SelectedWorkspace is null)
        {
            return;
        }

        var workspaceId = _appState.GetWorkspaceId();
        var response = await _dashboardsClient.GetDashboardList(workspaceId, cancellationToken);
        Dashboards = new ObservableCollection<DashboardResponse>(response);

        if (response.Count == 0)
        {
            _appSettingsStore.Update(s => s with { DefaultDashboard = null });
            return;
        }

        if (SelectedDashboard is null)
        {
            var defaultDashoardId = _appSettingsStore.ClientSettings.DefaultDashboard;
            if (defaultDashoardId is not null && response.Any(d => d.Id == defaultDashoardId))
            {
                SelectedDashboard = response.First(d => d.Id == defaultDashoardId);
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
                SelectNewDashboard(response.First());
            }
        }

        var printers = await _printersClient.GetPrintersList(workspaceId, new GetPrintersQuery(Enabled: true), cancellationToken);
        Printers = new ObservableCollection<PrinterDashboardCardViewModel>(printers.Select(printer => new PrinterDashboardCardViewModel(new PrinterStatusWidget()
        {
            Id = printer.Id,
            Name = printer.Name
        })));
    }

    [RelayCommand]
    private Task RefreshPrinters(CancellationToken cancellationToken) => LoadData(cancellationToken);

    private void SelectNewDashboard(DashboardResponse dashboard)
    {
        SelectedDashboard = dashboard;
        _appSettingsStore.Update(s => s with { DefaultDashboard = dashboard.Id });
    }
}
