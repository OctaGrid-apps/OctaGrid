using Avalonia.Controls;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Views;

[ViewFor(typeof(WorkspaceListViewModel))]
public partial class WorkspaceListView : ContentPage
{
    public WorkspaceListView()
    {
        InitializeComponent();
        // The owner of the page supplies its view model (see NavigationRailItem.PageDataContext).
        Loaded += async (_, _) =>
        {
            if (DataContext is WorkspaceListViewModel viewModel)
            {
                await viewModel.LoadDataCommand.ExecuteAsync(null);
            }
        };
    }
}
