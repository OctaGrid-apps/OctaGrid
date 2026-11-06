using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Metadata;
using Avalonia.Platform;

namespace OctaGrid.Client.Controls;

public class NavigationRailItem : AvaloniaObject
{
    public static readonly DirectProperty<NavigationRailItem, object?> EffectiveHeaderProperty =
        AvaloniaProperty.RegisterDirect<NavigationRailItem, object?>(
            nameof(EffectiveHeader),
            o => o.EffectiveHeader);

    public static readonly DirectProperty<NavigationRailItem, object?> EffectiveIconProperty =
        AvaloniaProperty.RegisterDirect<NavigationRailItem, object?>(
            nameof(EffectiveIcon),
            o => o.EffectiveIcon);

    public static readonly StyledProperty<object?> HeaderProperty =
        AvaloniaProperty.Register<NavigationRailItem, object?>(nameof(Header));

    public static readonly StyledProperty<CornerRadius> IconCornerRadiusProperty =
        AvaloniaProperty.Register<NavigationRailItem, CornerRadius>(nameof(IconCornerRadius));

    public static readonly StyledProperty<object?> IconProperty =
        AvaloniaProperty.Register<NavigationRailItem, object?>(nameof(Icon));

    public static readonly StyledProperty<bool> IsEnabledProperty =
        AvaloniaProperty.Register<NavigationRailItem, bool>(nameof(IsEnabled), defaultValue: true);

    public static readonly StyledProperty<bool> IsVisibleProperty =
        AvaloniaProperty.Register<NavigationRailItem, bool>(nameof(IsVisible), defaultValue: true);

    public static readonly StyledProperty<Control?> PageProperty =
                                    AvaloniaProperty.Register<NavigationRailItem, Control?>(nameof(Page));

    public static readonly StyledProperty<Type?> PageTypeProperty =
        AvaloniaProperty.Register<NavigationRailItem, Type?>(nameof(PageType));

    public static readonly StyledProperty<object?> PageDataContextProperty =
        AvaloniaProperty.Register<NavigationRailItem, object?>(nameof(PageDataContext));

    public static readonly StyledProperty<bool> ShowBadgeProperty =
        AvaloniaProperty.Register<NavigationRailItem, bool>(nameof(ShowBadge));

    private object? _effectiveHeader;
    private object? _effectiveIcon;

    public object? EffectiveHeader
    {
        get => _effectiveHeader;
        private set => SetAndRaise(EffectiveHeaderProperty, ref _effectiveHeader, value);
    }

    public object? EffectiveIcon
    {
        get => _effectiveIcon;
        private set => SetAndRaise(EffectiveIconProperty, ref _effectiveIcon, value);
    }

    /// <summary>
    /// Explicit menu item label. If null, falls back to NavigationRail.Header or Page.Header.
    /// </summary>
    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>
    /// Explicit menu item icon (StreamGeometry, IImage/Bitmap, "avares://..." asset URI, byte[], SVG path string, or Control).
    /// If null, falls back to NavigationRail.Icon or Page.Icon.
    /// </summary>
    public object? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>
    /// Corner radius applied when rendering image icons (e.g. set to "12" or "999" for a circular user avatar).
    /// </summary>
    public CornerRadius IconCornerRadius
    {
        get => GetValue(IconCornerRadiusProperty);
        set => SetValue(IconCornerRadiusProperty, value);
    }

    /// <summary>
    /// Gets or sets whether this navigation item is enabled.
    /// </summary>
    public bool IsEnabled
    {
        get => GetValue(IsEnabledProperty);
        set => SetValue(IsEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets whether this navigation item is visible in the rail.
    /// </summary>
    public bool IsVisible
    {
        get => GetValue(IsVisibleProperty);
        set => SetValue(IsVisibleProperty, value);
    }

    /// <summary>
    /// The Page or Control displayed when this menu item is selected.
    /// Marked with [Content] so you can place the page directly inside the XAML tag.
    /// </summary>
    [Content]
    public Control? Page
    {
        get => GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    /// <summary>
    /// Type of the Control/Page to instantiate lazily when this menu item is first selected.
    /// </summary>
    public Type? PageType
    {
        get => GetValue(PageTypeProperty);
        set => SetValue(PageTypeProperty, value);
    }

    /// <summary>
    /// Displays the small blue notification dot in the top-right of the icon.
    /// </summary>
    public bool ShowBadge
    {
        get => GetValue(ShowBadgeProperty);
        set => SetValue(ShowBadgeProperty, value);
    }

    /// <summary>
    /// The data context given to the page when it is created from <see cref="PageType"/>. The owner of
    /// the rail supplies it (usually from its own view model), so pages never look their view model up
    /// from a container themselves.
    /// </summary>
    public object? PageDataContext
    {
        get => GetValue(PageDataContextProperty);
        set => SetValue(PageDataContextProperty, value);
    }

    public Control? EnsurePage()
    {
        if (Page is null && PageType is not null)
        {
            var page = (Control)Activator.CreateInstance(PageType)!;
            if (PageDataContext is not null)
            {
                page.DataContext = PageDataContext;
            }

            Page = page;
        }

        return Page;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == PageProperty)
        {
            if (change.OldValue is AvaloniaObject oldPage)
            {
                oldPage.PropertyChanged -= OnPagePropertyChanged;
            }

            if (change.NewValue is AvaloniaObject newPage)
            {
                newPage.PropertyChanged += OnPagePropertyChanged;
            }

            UpdateEffectiveValues();
        }
        else if (change.Property == HeaderProperty || change.Property == IconProperty || change.Property == PageTypeProperty)
        {
            UpdateEffectiveValues();
        }
    }

    private static object? GetPageHeader(Control? page)
    {
        if (page is null)
        {
            return null;
        }

        var attachedHeader = NavigationRail.GetHeader(page);
        if (attachedHeader is not null)
        {
            return attachedHeader;
        }

        if (page is Page { Header: { } pageHeader })
        {
            return pageHeader;
        }

        var prop = page.GetType().GetProperty(nameof(ContentPage.Header)) ?? page.GetType().GetProperty(nameof(Window.Title));
        return prop?.GetValue(page);
    }

    private static object? GetPageIcon(Control? page)
    {
        if (page is null)
        {
            return null;
        }

        // 1. Check attached property NavigationRail.Icon first (supports StreamGeometry, strings, etc.)
        var attachedIcon = NavigationRail.GetIcon(page);
        if (attachedIcon is not null)
        {
            return attachedIcon;
        }

        // 2. Built-in Avalonia Page.Icon
        if (page is Page { Icon: { } pageIcon })
        {
            return pageIcon;
        }

        // 3. Fallback for custom property named "Icon" or "StreamGeometry" on the page
        var prop = page.GetType().GetProperty("Icon") ?? page.GetType().GetProperty("StreamGeometry");
        return prop?.GetValue(page);
    }

    private static object? ResolveIcon(object? rawIcon)
    {
        switch (rawIcon)
        {
            case null:
                return null;

            case byte[] { Length: > 0 } bytes:
                {
                    using var stream = new MemoryStream(bytes);
                    return new Bitmap(stream);
                }

            case Uri uri when uri.IsAbsoluteUri && uri.Scheme == "avares":
                {
                    using var stream = AssetLoader.Open(uri);
                    return new Bitmap(stream);
                }

            case string str when !string.IsNullOrWhiteSpace(str):
                {
                    if (str.StartsWith("avares://", StringComparison.OrdinalIgnoreCase) &&
                        Uri.TryCreate(str, UriKind.Absolute, out var assetUri))
                    {
                        using var stream = AssetLoader.Open(assetUri);
                        return new Bitmap(stream);
                    }

                    if (File.Exists(str))
                    {
                        return new Bitmap(str);
                    }

                    return rawIcon;
                }

            default:
                return rawIcon;
        }
    }

    private void OnPagePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Avalonia.Controls.Page.HeaderProperty ||
            e.Property == Avalonia.Controls.Page.IconProperty ||
            e.Property == NavigationRail.HeaderProperty ||
            e.Property == NavigationRail.IconProperty ||
            e.Property.Name is "Header" or "Title" or "Icon" or "StreamGeometry")
        {
            UpdateEffectiveValues();
        }
    }

    private void UpdateEffectiveValues()
    {
        var header = Header;
        if (header is null && Page is not null)
        {
            header = GetPageHeader(Page);
        }

        var icon = Icon;
        if (icon is null && Page is not null)
        {
            icon = GetPageIcon(Page);
        }

        EffectiveHeader = header;
        EffectiveIcon = ResolveIcon(icon);
    }
}
