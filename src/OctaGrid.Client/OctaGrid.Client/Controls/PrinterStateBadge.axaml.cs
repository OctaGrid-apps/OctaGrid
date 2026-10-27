using Avalonia;
using Avalonia.Controls;
using OctaGrid.Client.Views;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Controls;

public partial class PrinterStateBadge : UserControl
{
    public static readonly StyledProperty<UniversalPrinterState> StateProperty =
    AvaloniaProperty.Register<MainView, UniversalPrinterState>(nameof(State), defaultValue: UniversalPrinterState.Offline);

    public PrinterStateBadge()
    {
        InitializeComponent();
    }

    public UniversalPrinterState State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }
}
