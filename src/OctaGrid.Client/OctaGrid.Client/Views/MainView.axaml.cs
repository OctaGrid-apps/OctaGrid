using Avalonia.Controls;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Views;

[ViewFor(typeof(MainViewModel))]
public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DataContext is not MainViewModel viewModel)
            {
                return;
            }

            if (TopLevel.GetTopLevel(this) is { } topLevel)
            {
                viewModel.AttachNotifications(topLevel);
            }

            await viewModel.LoadDataCommand.ExecuteAsync(null);
        };
    }
}
