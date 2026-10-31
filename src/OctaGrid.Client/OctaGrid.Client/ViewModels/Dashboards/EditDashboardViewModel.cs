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

    public EditDashboardViewModel() : this(new DashboardViewModel(), App.Services.GetService<ILogger<EditDashboardViewModel>>() ?? NullLogger<EditDashboardViewModel>.Instance)
    {
    }

    public EditDashboardViewModel(DashboardViewModel dashboard, ILogger<EditDashboardViewModel> logger)
    {
        _logger = logger;
        Item = dashboard;
        Item.ErrorsChanged += (s, e) => ConfirmCommand.NotifyCanExecuteChanged();
    }

    [ObservableProperty]
    public partial DashboardViewModel Item { get; private set; }

    [RelayCommand]
    private void Cancel()
    {
        _logger.LogDebug("EditDashboard dialog cancelled by user");
        this.ReturnResultFromOverlayDialog(null);
    }

    [RelayCommand(CanExecute = nameof(IsItemValid))]
    private void Confirm()
    {
        _logger.LogDebug("EditDashboard dialog confirmed with DashboardName: {DashboardName}", Item.DashboardName);
        this.ReturnResultFromOverlayDialog(Item.DashboardName);
    }

    private bool IsItemValid() => !Item.HasErrors && !string.IsNullOrWhiteSpace(Item.DashboardName);
}
