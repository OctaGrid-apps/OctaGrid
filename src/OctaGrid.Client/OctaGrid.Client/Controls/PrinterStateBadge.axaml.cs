using Avalonia;
using Avalonia.Controls;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Client.Controls;

public partial class PrinterStateBadge : UserControl
{
    public PrinterStateBadge()
    {
        InitializeComponent();
    }

    /// <summary>Optional number shown after the state name, such as how many printers are in that state.</summary>
    [GeneratedStyledProperty]
    public partial int? Count { get; set; }

    [GeneratedStyledProperty(DefaultValue = UniversalPrinterState.Offline)]
    public partial UniversalPrinterState State { get; set; }
}
