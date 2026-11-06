using Avalonia.Controls;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Views;

[ViewFor(typeof(AgentListViewModel))]
public partial class AgentListView : ContentPage
{
    public AgentListView()
    {
        InitializeComponent();
        // The owner of the page supplies its view model (see NavigationRailItem.PageDataContext).
        Loaded += async (_, _) =>
        {
            if (DataContext is AgentListViewModel viewModel)
            {
                await viewModel.LoadDataCommand.ExecuteAsync(null);
            }
        };
    }
}
