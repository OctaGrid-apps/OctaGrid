using System.Collections.Specialized;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Metadata;
using Avalonia.VisualTree;

namespace OctaGrid.Client.Controls;

[TemplatePart("PART_TopListBox", typeof(ListBox))]
[TemplatePart("PART_FooterListBox", typeof(ListBox))]
public partial class NavigationRail : TemplatedControl
{
    private ListBox? _footerListBox;

    private bool _isSyncingSelection;

    private ListBox? _topListBox;

    public NavigationRail()
    {
        Items.CollectionChanged += OnItemsChanged;
        FooterItems.CollectionChanged += OnItemsChanged;
    }

    [GeneratedStyledProperty]
    public partial IBrush? ContentBackground { get; set; }

    /// <summary>The page of the selected item. Only the rail sets it, so it is a direct property and read-only outside.</summary>
    [GeneratedDirectProperty]
    public partial Control? CurrentPage { get; private set; }

    /// <summary>
    /// A page opened on top of <see cref="CurrentPage" />, for example a printer's page. The current page stays
    /// alive underneath, so closing the detail brings back the page exactly as the user left it. It is cleared when an
    /// item of the rail is chosen, including the one that is already selected.
    /// </summary>
    [GeneratedStyledProperty(DefaultBindingMode = Avalonia.Data.BindingMode.TwoWay)]
    public partial object? DetailContent { get; set; }

    [GeneratedDirectProperty]
    public partial AvaloniaList<NavigationRailItem> FooterItems { get; private set; } = [];

    /// <summary>
    /// When true, hides the text label of the active item and centers its icon.
    /// </summary>
    [GeneratedStyledProperty]
    public partial bool HideHeaderWhenSelected { get; set; }

    [Content]
    [GeneratedDirectProperty]
    public partial AvaloniaList<NavigationRailItem> Items { get; private set; } = [];

    [GeneratedStyledProperty]
    public partial IBrush? PaneBackground { get; set; }

    [GeneratedStyledProperty]
    public partial object? PaneHeader { get; set; }

    [GeneratedStyledProperty]
    public partial IDataTemplate? PaneHeaderTemplate { get; set; }

    [GeneratedStyledProperty(DefaultValue = 72.0)]
    public partial double PaneWidth { get; set; }

    [GeneratedStyledProperty]
    public partial NavigationRailItem? SelectedItem { get; set; }

    /// <summary>
    /// Attached property to set a header label directly on any Control (such as a UserControl).
    /// </summary>
    [GeneratedAttachedProperty]
    public static partial object? GetHeader(Control element);

    /// <summary>
    /// Attached property to set an icon (StreamGeometry, Geometry, PathIcon, string path, IImage)
    /// directly on any Page or UserControl.
    /// </summary>
    [GeneratedAttachedProperty]
    public static partial object? GetIcon(Control element);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _topListBox?.SelectionChanged -= OnTopListSelectionChanged;
        _footerListBox?.SelectionChanged -= OnFooterListSelectionChanged;
        _topListBox?.RemoveHandler(TappedEvent, OnItemTapped);
        _footerListBox?.RemoveHandler(TappedEvent, OnItemTapped);

        _topListBox = e.NameScope.Find<ListBox>("PART_TopListBox");
        _footerListBox = e.NameScope.Find<ListBox>("PART_FooterListBox");

        _topListBox?.SelectionChanged += OnTopListSelectionChanged;
        _footerListBox?.SelectionChanged += OnFooterListSelectionChanged;
        _topListBox?.AddHandler(TappedEvent, OnItemTapped);
        _footerListBox?.AddHandler(TappedEvent, OnItemTapped);

        if (SelectedItem is null || (!Items.Contains(SelectedItem) && !FooterItems.Contains(SelectedItem)))
        {
            SelectedItem = Items.FirstOrDefault() ?? FooterItems.FirstOrDefault();
        }

        CurrentPage = SelectedItem?.EnsurePage();
        SyncListBoxesWithSelectedItem(SelectedItem);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SelectedItemProperty)
        {
            if (change.OldValue is NavigationRailItem oldItem)
            {
                oldItem.PropertyChanged -= OnSelectedItemPropertyChanged;
            }

            var newItem = change.GetNewValue<NavigationRailItem?>();
            newItem?.PropertyChanged += OnSelectedItemPropertyChanged;

            DetailContent = null;
            CurrentPage = newItem?.EnsurePage();
            SyncListBoxesWithSelectedItem(newItem);
        }
    }

    // Choosing the item that is already selected raises no selection change, yet it must still leave the detail page.
    private void OnItemTapped(object? sender, TappedEventArgs e)
    {
        if (DetailContent is not null && (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>() is not null)
        {
            DetailContent = null;
        }
    }

    private void OnFooterListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingSelection)
        {
            return;
        }

        if (_footerListBox?.SelectedItem is NavigationRailItem item)
        {
            _isSyncingSelection = true;
            _topListBox?.SelectedItem = null;
            SelectedItem = item;
            _isSyncingSelection = false;
        }
        else if (_topListBox?.SelectedItem is null && SelectedItem is not null && FooterItems.Contains(SelectedItem))
        {
            // Prevent accidental deselection when clicking the already-selected item
            SyncListBoxesWithSelectedItem(SelectedItem);
        }
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_topListBox is null && _footerListBox is null)
        {
            return;
        }

        if (SelectedItem is null || (!Items.Contains(SelectedItem) && !FooterItems.Contains(SelectedItem)))
        {
            SelectedItem = Items.FirstOrDefault() ?? FooterItems.FirstOrDefault();
        }
        else
        {
            SyncListBoxesWithSelectedItem(SelectedItem);
        }
    }

    private void OnSelectedItemPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == NavigationRailItem.PageProperty && ReferenceEquals(sender, SelectedItem))
        {
            CurrentPage = SelectedItem?.Page;
        }
    }

    private void OnTopListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingSelection)
        {
            return;
        }

        if (_topListBox?.SelectedItem is NavigationRailItem item)
        {
            _isSyncingSelection = true;
            _footerListBox?.SelectedItem = null;
            SelectedItem = item;
            _isSyncingSelection = false;
        }
        else if (_footerListBox?.SelectedItem is null && SelectedItem is not null && Items.Contains(SelectedItem))
        {
            // Prevent accidental deselection when clicking the already-selected item
            SyncListBoxesWithSelectedItem(SelectedItem);
        }
    }

    private void SyncListBoxesWithSelectedItem(NavigationRailItem? item)
    {
        if (_isSyncingSelection)
        {
            return;
        }

        _isSyncingSelection = true;
        _topListBox?.SelectedItem = item is not null && Items.Contains(item) ? item : null;
        _footerListBox?.SelectedItem = item is not null && FooterItems.Contains(item) ? item : null;
        _isSyncingSelection = false;
    }
}
