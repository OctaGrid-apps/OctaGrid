using Avalonia;
using Avalonia.Controls;
using OctaGrid.Client.Views;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Controls;

public partial class PrinterStateBadge : UserControl
{
    public static readonly StyledProperty<UniversalPrinterState> StateProperty =
    AvaloniaProperty.Register<PrinterStateBadge, UniversalPrinterState>(nameof(State), defaultValue: UniversalPrinterState.Offline);

    /// <summary>Optional number shown after the state name, such as how many printers are in that state.</summary>
    public static readonly StyledProperty<int?> CountProperty =
        AvaloniaProperty.Register<PrinterStateBadge, int?>(nameof(Count));

    public PrinterStateBadge()
    {
        InitializeComponent();
    }

    public int? Count
    {
        get => GetValue(CountProperty);
        set => SetValue(CountProperty, value);
    }

    public UniversalPrinterState State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }
}
