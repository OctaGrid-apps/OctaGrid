using Avalonia.Controls;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Dialogs;

[ViewFor(typeof(ExtensionTrustViewModel))]
public partial class ExtensionTrustView : UserControl
{
    public ExtensionTrustView()
    {
        InitializeComponent();
    }
}
