using Avalonia.Controls;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Dialogs;

[ViewFor(typeof(SafetyDisclaimerViewModel))]
public partial class SafetyDisclaimerView : UserControl
{
    public SafetyDisclaimerView()
    {
        InitializeComponent();
    }
}
