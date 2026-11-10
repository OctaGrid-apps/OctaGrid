using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Controls;

/// <summary>
/// The lines of a printer's console with its loading, error and empty states. It follows the newest line while the user
/// is at the bottom, and stays where it is once they scroll up to read. It only shows what its view model holds.
/// </summary>
public partial class PrinterConsolePanel : UserControl
{
    // A little slack: the scroll position is a fraction, and a partly visible last line still counts as the bottom.
    private const double BottomTolerance = 24;

    private bool _isAttached;
    private PrinterConsoleViewModel? _viewModel;

    public PrinterConsolePanel()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        Subscribe(DataContext as PrinterConsoleViewModel);
        ScrollToEndLater();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // The view model outlives the panel (the tab content is created again when the tab is shown again), so a panel
        // that is gone must not stay subscribed to it.
        _isAttached = false;
        Subscribe(null);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_isAttached)
        {
            Subscribe(DataContext as PrinterConsoleViewModel);
        }
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add)
        {
            return;
        }

        // Measured before the new line is laid out: if the user was at the bottom, they stay there.
        var scroller = this.FindControl<ScrollViewer>("LinesScroller");
        if (scroller is null || scroller.Offset.Y + scroller.Viewport.Height >= scroller.Extent.Height - BottomTolerance)
        {
            ScrollToEndLater();
        }
    }

    private void ScrollToEndLater() =>
        Dispatcher.UIThread.Post(() => this.FindControl<ScrollViewer>("LinesScroller")?.ScrollToEnd(), DispatcherPriority.Background);

    private void Subscribe(PrinterConsoleViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        _viewModel?.Lines.CollectionChanged -= OnLinesChanged;
        _viewModel = viewModel;
        _viewModel?.Lines.CollectionChanged += OnLinesChanged;
    }
}
