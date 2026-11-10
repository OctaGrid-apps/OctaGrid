using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Controls;

/// <summary>
/// The lines of a printer's console with its loading, error and empty states, newest at the top. While the user is at the
/// top, new lines appear there and are seen at once. Once they scroll down to read older lines, the view moves along with
/// the lines that are pushed down, so what they are reading stays where it is. It only shows what its view model holds.
/// </summary>
public partial class PrinterConsolePanel : UserControl
{
    // A little slack: the scroll position is a fraction, and a line that is partly scrolled away still counts as the top.
    private const double TopTolerance = 8;

    private bool _isAttached;
    private bool _anchorPending;
    private object? _anchorItem;
    private double _anchorFromTop;
    private double _anchorExtent;
    private double _anchorOffset;
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

    // Putting text on the clipboard needs the window, so it is done here and not in the view model.
    private async void CopyButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.GetTranscript() is not { Length: > 0 } text || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        try
        {
            await clipboard.SetTextAsync(text);
        }
        catch (Exception ex)
        {
            // The clipboard can be held by another program for a moment; nothing is lost by trying again.
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Only a line added at the top moves what the user sees; lines dropped at the bottom do not.
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewStartingIndex != 0)
        {
            return;
        }

        var list = this.FindControl<ItemsControl>("LinesList");
        var scroller = list?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (list is null || scroller is null || scroller.Offset.Y <= TopTolerance)
        {
            // At the top the newest line is simply in view, however many arrive.
            return;
        }

        // Several lines can arrive before the next layout; the place to keep is taken once, before any of them is laid out.
        if (!_anchorPending)
        {
            _anchorPending = true;
            CaptureAnchor(list, scroller);
            Dispatcher.UIThread.Post(() => KeepPlace(list, scroller), DispatcherPriority.Background);
        }
    }

    // Remembers which row is at the top of what the user sees and how far below the top edge it is. The row is what has to
    // stay where it is: the size of a virtualized list is an estimate (rows that are not made yet count as an average
    // row, and wrapped rows differ in height), so how much the list grew says little about how far the row moved.
    private void CaptureAnchor(ItemsControl list, ScrollViewer scroller)
    {
        _anchorExtent = scroller.Extent.Height;
        _anchorOffset = scroller.Offset.Y;

        var top = list.GetRealizedContainers()
            .Where(c => c.Bounds.Bottom > scroller.Offset.Y)
            .OrderBy(c => c.Bounds.Y)
            .FirstOrDefault();
        _anchorItem = top?.DataContext;
        _anchorFromTop = top is null ? 0 : top.Bounds.Y - scroller.Offset.Y;
    }

    private void KeepPlace(ItemsControl list, ScrollViewer scroller)
    {
        _anchorPending = false;
        var anchorItem = _anchorItem;
        _anchorItem = null;

        if (anchorItem is not null)
        {
            // Lines added above can push the row far out of the part that is in view, where it is no longer made and
            // where the size of the list is only an estimate. Bringing it into view makes it, and measures it.
            var row = list.ContainerFromItem(anchorItem);
            if (row is null)
            {
                list.ScrollIntoView(anchorItem);
                row = list.ContainerFromItem(anchorItem);
            }

            if (row is not null)
            {
                scroller.Offset = new Vector(scroller.Offset.X, row.Bounds.Y - _anchorFromTop);
                return;
            }
        }

        // The row cannot be made: the growth of the list is the best that is known, and the next line that arrives
        // corrects it.
        var added = scroller.Extent.Height - _anchorExtent;
        if (added > 0)
        {
            scroller.Offset = new Vector(scroller.Offset.X, _anchorOffset + added);
        }
    }

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
