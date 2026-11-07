using Avalonia.Controls;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Dialogs;

[ViewFor(typeof(ConfirmViewModel))]
public partial class ConfirmView : UserControl
{
    public ConfirmView()
    {
        InitializeComponent();
    }
}
