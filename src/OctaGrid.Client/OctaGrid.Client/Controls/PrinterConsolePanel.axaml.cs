using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
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

        var scroller = this.FindControl<ScrollViewer>("LinesScroller");
        if (scroller is null || scroller.Offset.Y <= TopTolerance)
        {
            // At the top the newest line is simply in view, however many arrive.
            return;
        }

        // Several lines can arrive before the next layout. They are measured against the same starting point, so the view
        // moves by exactly what was added in total.
        if (!_anchorPending)
        {
            _anchorPending = true;
            _anchorExtent = scroller.Extent.Height;
            _anchorOffset = scroller.Offset.Y;
            Dispatcher.UIThread.Post(() => KeepPlace(scroller), DispatcherPriority.Background);
        }
    }

    private void KeepPlace(ScrollViewer scroller)
    {
        _anchorPending = false;
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
