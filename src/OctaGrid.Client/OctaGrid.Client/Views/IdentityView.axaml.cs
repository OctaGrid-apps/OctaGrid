using Avalonia.Controls;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Views;

[ViewFor(typeof(IdentityViewModel))]
public partial class IdentityView : Window
{
    public IdentityView()
    {
        InitializeComponent();
    }
}
