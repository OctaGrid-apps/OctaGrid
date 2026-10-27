using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Views;

[ViewFor(typeof(ModeSelectionViewModel))]
public partial class ModeSelectionWindow : Window
{
    public ModeSelectionWindow()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<ModeSelectionViewModel>();
    }
}
