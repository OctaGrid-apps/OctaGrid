using Avalonia.Controls;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Dialogs;

[ViewFor(typeof(PrintStartViewModel))]
public partial class PrintStartView : UserControl
{
    public PrintStartView()
    {
        InitializeComponent();
    }
}
