using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OctaGrid.Client.ViewModels;

public partial class DashboardViewModel : ViewModelValidatorBase
{
    public DashboardViewModel(string dashboardName = "")
    {
        DashboardName = dashboardName;
    }

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required, Length(2, 100)]
    public partial string? DashboardName { get; set; }
}
