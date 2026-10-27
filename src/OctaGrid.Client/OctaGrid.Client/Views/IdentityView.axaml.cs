using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Views;

[ViewFor(typeof(IdentityViewModel))]
public partial class IdentityView : Window
{
    public IdentityView()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<IdentityViewModel>();
    }
}
