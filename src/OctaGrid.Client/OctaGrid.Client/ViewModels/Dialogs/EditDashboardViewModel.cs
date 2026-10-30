using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Services;

namespace OctaGrid.Client.ViewModels;

public partial class EditDashboardViewModel : ViewModelBase, IDialogParticipant<string>
{
    private readonly ILogger<EditDashboardViewModel> _logger;

    public EditDashboardViewModel() : this(App.Services.GetService<ILogger<EditDashboardViewModel>>() ?? NullLogger<EditDashboardViewModel>.Instance)
    {
    }

    public EditDashboardViewModel(ILogger<EditDashboardViewModel> logger)
    {
        _logger = logger;
        _logger.LogDebug("EditDashboardViewModel initialized");
    }

    [ObservableProperty]
    public partial string DashboardName { get; set; }

    [RelayCommand]
    private void Cancel()
    {
        _logger.LogDebug("EditDashboard dialog cancelled by user");
        this.ReturnResultFromOverlayDialog(null);
    }

    [RelayCommand]
    private void Confirm()
    {
        _logger.LogDebug("EditDashboard dialog confirmed with DashboardName: {DashboardName}", DashboardName);
        this.ReturnResultFromOverlayDialog(DashboardName);
    }
}
