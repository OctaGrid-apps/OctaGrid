using Avalonia.Controls;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Dialogs;

[ViewFor(typeof(EditPrinterViewModel))]
public partial class EditPrinterView : UserControl
{
    public EditPrinterView()
    {
        InitializeComponent();
    }
}
