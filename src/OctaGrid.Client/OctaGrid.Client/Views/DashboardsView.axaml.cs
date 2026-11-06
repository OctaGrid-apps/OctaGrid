using Avalonia.Controls;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Views;

[ViewFor(typeof(DashboardsViewModel))]
public partial class DashboardsView : ContentPage
{
    public DashboardsView()
    {
        InitializeComponent();
        // The owner of the page supplies its view model (see NavigationRailItem.PageDataContext).
        Loaded += async (_, _) =>
        {
            if (DataContext is DashboardsViewModel viewModel)
            {
                await viewModel.LoadDataCommand.ExecuteAsync(null);
            }
        };
    }
}
