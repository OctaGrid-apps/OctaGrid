using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace OctaGrid.Client.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = $"OctaGrid {Helper.GetAppVersion()}";
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        HideBuiltInDrawnTitle();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsExtendedIntoWindowDecorationsProperty)
        {
            HideBuiltInDrawnTitle();
        }
    }

    private void HideBuiltInDrawnTitle()
    {
        if (this.GetVisualParent() is not Visual topLevelHost)
        {
            return;
        }

        if (topLevelHost is ILogical logicalHost)
        {
            foreach (var decorations in logicalHost.LogicalChildren.OfType<WindowDrawnDecorations>())
            {
                decorations.SetCurrentValue(WindowDrawnDecorations.TitleProperty, string.Empty);
            }
        }

        foreach (var panel in topLevelHost.GetVisualDescendants().OfType<Panel>())
        {
            if (panel.Name == "PART_TitleTextPanel")
            {
                panel.Children.Clear();
            }
        }
    }
}
