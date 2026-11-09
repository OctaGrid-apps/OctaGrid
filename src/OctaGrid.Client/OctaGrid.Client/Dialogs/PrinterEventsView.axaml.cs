using Avalonia.Controls;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Dialogs;

[ViewFor(typeof(PrinterEventsViewModel))]
public partial class PrinterEventsView : UserControl
{
    public PrinterEventsView()
    {
        InitializeComponent();

        // The dialog can be dismissed without the Close button (Escape, a click outside), which never reaches the view model.
        Unloaded += (_, _) => (DataContext as PrinterEventsViewModel)?.Stop();
        Loaded += async (_, _) =>
        {
            if (DataContext is PrinterEventsViewModel viewModel)
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
            }
        };
    }
}
