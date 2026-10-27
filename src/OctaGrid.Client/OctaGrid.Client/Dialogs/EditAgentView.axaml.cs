using Avalonia.Controls;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Dialogs;

[ViewFor(typeof(EditAgentViewModel))]
public partial class EditAgentView : UserControl
{
    public EditAgentView()
    {
        InitializeComponent();
    }
}
