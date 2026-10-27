using Avalonia.Controls;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Dialogs;

[ViewFor(typeof(EditDashboardViewModel))]
public partial class EditDashboardView : UserControl
{
    public EditDashboardView()
    {
        InitializeComponent();
    }
}
