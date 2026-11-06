using Avalonia.Controls;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Views;

[ViewFor(typeof(ModeSelectionViewModel))]
public partial class ModeSelectionWindow : Window
{
    public ModeSelectionWindow()
    {
        InitializeComponent();
    }
}
