using Avalonia.Controls;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Dialogs;

[ViewFor(typeof(ExtensionsViewModel))]
public partial class ExtensionsView : UserControl
{
    public ExtensionsView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DataContext is ExtensionsViewModel viewModel)
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
            }
        };
    }
}
