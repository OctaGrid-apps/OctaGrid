using Avalonia.Controls;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Controls;

[ViewFor(typeof(PrinterDashboardCardViewModel))]
public partial class PrinterDashboardCard : UserControl
{
    public PrinterDashboardCard()
    {
        InitializeComponent();
    }
}
