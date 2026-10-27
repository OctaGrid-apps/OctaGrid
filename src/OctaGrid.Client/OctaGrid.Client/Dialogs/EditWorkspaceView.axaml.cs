using Avalonia.Controls;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Dialogs;

[ViewFor(typeof(EditWorkspaceViewModel))]
public partial class EditWorkspaceView : UserControl
{
    public EditWorkspaceView()
    {
        InitializeComponent();
    }
}
