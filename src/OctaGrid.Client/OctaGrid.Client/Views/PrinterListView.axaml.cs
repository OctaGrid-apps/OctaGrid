using Avalonia.Controls;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Views;

[ViewFor(typeof(PrinterListViewModel))]
public partial class PrinterListView : ContentPage
{
    public PrinterListView()
    {
        InitializeComponent();
        // The owner of the page supplies its view model (see NavigationRailItem.PageDataContext).
        Loaded += async (_, _) =>
        {
            if (DataContext is PrinterListViewModel viewModel)
            {
                await viewModel.LoadDataCommand.ExecuteAsync(null);
            }
        };
    }
}
