using System.Collections.Specialized;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Media;
using Avalonia.Metadata;

namespace OctaGrid.Client.Controls;

[TemplatePart("PART_TopListBox", typeof(ListBox))]
[TemplatePart("PART_FooterListBox", typeof(ListBox))]
public class NavigationRail : TemplatedControl
{
    public static readonly StyledProperty<IBrush?> ContentBackgroundProperty =
            AvaloniaProperty.Register<NavigationRail, IBrush?>(nameof(ContentBackground));

    public static readonly StyledProperty<Control?> CurrentPageProperty =
            AvaloniaProperty.Register<NavigationRail, Control?>(nameof(CurrentPage));

    /// <summary>
    /// A page opened on top of <see cref="CurrentPage" />, for example a printer's page. The current page stays
    /// alive underneath, so closing the detail brings back the page exactly as the user left it. It is cleared when an
    /// item of the rail is chosen, including the one that is already selected.
    /// </summary>
    public static readonly StyledProperty<object?> DetailContentProperty =
            AvaloniaProperty.Register<NavigationRail, object?>(
                nameof(DetailContent),
                defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly DirectProperty<NavigationRail, AvaloniaList<NavigationRailItem>> FooterItemsProperty =
            AvaloniaProperty.RegisterDirect<NavigationRail, AvaloniaList<NavigationRailItem>>(
                nameof(FooterItems),
                o => o.FooterItems);

    /// <summary>
    /// Attached property to set a header label directly on any Control (such as a UserControl).
    /// </summary>
    public static readonly AttachedProperty<object?> HeaderProperty =
        AvaloniaProperty.RegisterAttached<NavigationRail, Control, object?>("Header");

    public static readonly StyledProperty<bool> HideHeaderWhenSelectedProperty =
            AvaloniaProperty.Register<NavigationRail, bool>(nameof(HideHeaderWhenSelected), defaultValue: false);

    /// <summary>
    /// Attached property to set an icon (StreamGeometry, Geometry, PathIcon, string path, IImage)
    /// directly on any Page or UserControl.
    /// </summary>
    public static readonly AttachedProperty<object?> IconProperty =
        AvaloniaProperty.RegisterAttached<NavigationRail, Control, object?>("Icon");

    public static readonly DirectProperty<NavigationRail, AvaloniaList<NavigationRailItem>> ItemsProperty =
        AvaloniaProperty.RegisterDirect<NavigationRail, AvaloniaList<NavigationRailItem>>(
            nameof(Items),
            o => o.Items);

    public static readonly StyledProperty<IBrush?> PaneBackgroundProperty =
        AvaloniaProperty.Register<NavigationRail, IBrush?>(nameof(PaneBackground));

    public static readonly StyledProperty<object?> PaneHeaderProperty =
        AvaloniaProperty.Register<NavigationRail, object?>(nameof(PaneHeader));

    public static readonly StyledProperty<IDataTemplate?> PaneHeaderTemplateProperty =
        AvaloniaProperty.Register<NavigationRail, IDataTemplate?>(nameof(PaneHeaderTemplate));

    public static readonly StyledProperty<double> PaneWidthProperty =
        AvaloniaProperty.Register<NavigationRail, double>(nameof(PaneWidth), defaultValue: 72.0);

    public static readonly StyledProperty<NavigationRailItem?> SelectedItemProperty =
        AvaloniaProperty.Register<NavigationRail, NavigationRailItem?>(nameof(SelectedItem));

    private readonly AvaloniaList<NavigationRailItem> _footerItems = [];

    private readonly AvaloniaList<NavigationRailItem> _items = [];

    private ListBox? _footerListBox;

    private bool _isSyncingSelection;

    private ListBox? _topListBox;

    public NavigationRail()
    {
        Items.CollectionChanged += OnItemsChanged;
        FooterItems.CollectionChanged += OnItemsChanged;
    }

    public IBrush? ContentBackground
    {
        get => GetValue(ContentBackgroundProperty);
        set => SetValue(ContentBackgroundProperty, value);
    }

    public Control? CurrentPage
    {
        get => GetValue(CurrentPageProperty);
        private set => SetValue(CurrentPageProperty, value);
    }

    public object? DetailContent
    {
        get => GetValue(DetailContentProperty);
        set => SetValue(DetailContentProperty, value);
    }

    public AvaloniaList<NavigationRailItem> FooterItems => _footerItems;

    /// <summary>
    /// When true, hides the text label of the active item and centers its icon.
    /// </summary>
    public bool HideHeaderWhenSelected
    {
        get => GetValue(HideHeaderWhenSelectedProperty);
        set => SetValue(HideHeaderWhenSelectedProperty, value);
    }

    [Content]
    public AvaloniaList<NavigationRailItem> Items => _items;

    public IBrush? PaneBackground
    {
        get => GetValue(PaneBackgroundProperty);
        set => SetValue(PaneBackgroundProperty, value);
    }

    public object? PaneHeader
    {
        get => GetValue(PaneHeaderProperty);
        set => SetValue(PaneHeaderProperty, value);
    }

    public IDataTemplate? PaneHeaderTemplate
    {
        get => GetValue(PaneHeaderTemplateProperty);
        set => SetValue(PaneHeaderTemplateProperty, value);
    }

    public double PaneWidth
    {
        get => GetValue(PaneWidthProperty);
        set => SetValue(PaneWidthProperty, value);
    }

    public NavigationRailItem? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public static object? GetHeader(Control element) => element.GetValue(HeaderProperty);

    public static object? GetIcon(Control element) => element.GetValue(IconProperty);

    public static void SetHeader(Control element, object? value) => element.SetValue(HeaderProperty, value);

    public static void SetIcon(Control element, object? value) => element.SetValue(IconProperty, value);

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
