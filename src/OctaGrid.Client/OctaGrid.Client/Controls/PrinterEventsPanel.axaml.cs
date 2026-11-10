using Avalonia.Controls;

namespace OctaGrid.Client.Controls;

/// <summary>
/// The list of a printer's events with its loading, error and empty states. It only shows what its view model holds:
/// whoever hosts it (the history dialog, the printer page) decides when the view model loads and stops.
/// </summary>
public partial class PrinterEventsPanel : UserControl
{
    public PrinterEventsPanel()
    {
        InitializeComponent();
    }
}
