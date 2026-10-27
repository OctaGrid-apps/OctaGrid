using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OctaGrid.Client.Services;

namespace OctaGrid.Client.ViewModels;

public partial class EditDashboardViewModel : ViewModelBase, IDialogParticipant<string>
{
    [ObservableProperty]
    public partial string DashboardName { get; set; }

    [RelayCommand]
    private void Cancel()
    {
        this.ReturnResultFromOverlayDialog(null);
    }

    [RelayCommand]
    private void Confirm()
    {
        this.ReturnResultFromOverlayDialog(DashboardName);
    }
}
