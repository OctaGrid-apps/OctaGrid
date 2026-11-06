using Avalonia.Controls;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Views;

public partial class SettingsView : ContentPage
{
    public SettingsView()
    {
        InitializeComponent();
        // The owner of the page supplies its view model (see NavigationRailItem.PageDataContext).
        Loaded += async (_, _) =>
        {
            if (DataContext is SettingsViewModel viewModel)
            {
                await viewModel.LoadDataCommand.ExecuteAsync(null);
            }
        };
    }
}
