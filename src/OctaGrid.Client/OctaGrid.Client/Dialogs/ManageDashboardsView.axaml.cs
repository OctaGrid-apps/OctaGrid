using Avalonia.Controls;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Dialogs;

[ViewFor(typeof(ManageDashboardsViewModel))]
public partial class ManageDashboardsView : UserControl
{
    public ManageDashboardsView()
    {
        InitializeComponent();
    }
}
