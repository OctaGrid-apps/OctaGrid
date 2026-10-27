using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace OctaGrid.Client.Controls;

/// <summary>
/// A Windows 11 Settings-style expandable card control that displays a header card
/// with an optional icon, description, right-aligned action content, and a collapsible
/// list of child <see cref="SettingsCard"/> items stacked underneath.
/// </summary>
[PseudoClasses(":expanded")]
[TemplatePart("PART_HeaderBorder", typeof(Border))]
[TemplatePart("PART_ActionPresenter", typeof(Control))]
[TemplatePart("PART_RestartBanner", typeof(Control))]
[TemplatePart("PART_FooterPresenter", typeof(Control))]
public class SettingsExpander : ItemsControl
{
    /// <summary>
    /// Defines the <see cref="Content"/> property for the right-aligned action control in the header card.
    /// </summary>
    public static readonly StyledProperty<object?> ContentProperty =
        AvaloniaProperty.Register<SettingsExpander, object?>(nameof(Content), defaultValue: null);

    /// <summary>
    /// Defines the <see cref="ContentTemplate"/> property for <see cref="Content"/>.
    /// </summary>
    public static readonly StyledProperty<IDataTemplate?> ContentTemplateProperty =
        AvaloniaProperty.Register<SettingsExpander, IDataTemplate?>(nameof(ContentTemplate), defaultValue: null);

    /// <summary>
    /// Defines the <see cref="Description"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<SettingsExpander, string?>(nameof(Description), defaultValue: null);

    /// <summary>
    /// Defines the <see cref="Footer"/> property.
    /// </summary>
    public static readonly StyledProperty<object?> FooterProperty =
        AvaloniaProperty.Register<SettingsExpander, object?>(nameof(Footer), defaultValue: null);

    /// <summary>
    /// Defines the <see cref="FooterTemplate"/> property.
    /// </summary>
    public static readonly StyledProperty<IDataTemplate?> FooterTemplateProperty =
        AvaloniaProperty.Register<SettingsExpander, IDataTemplate?>(nameof(FooterTemplate), defaultValue: null);

    /// <summary>
    /// Defines the <see cref="Header"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> HeaderProperty =
        AvaloniaProperty.Register<SettingsExpander, string?>(nameof(Header), defaultValue: null);

    /// <summary>
    /// Defines the <see cref="Icon"/> property.
    /// </summary>
    public static readonly StyledProperty<StreamGeometry?> IconProperty =
        AvaloniaProperty.Register<SettingsExpander, StreamGeometry?>(nameof(Icon), defaultValue: null);

    /// <summary>
    /// Defines the <see cref="IsExpanded"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<SettingsExpander, bool>(
            nameof(IsExpanded),
            defaultValue: false,
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Defines the <see cref="IsFooterVisible"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsFooterVisibleProperty =
        AvaloniaProperty.Register<SettingsExpander, bool>(nameof(IsFooterVisible), defaultValue: true);

    /// <summary>
    /// Defines the <see cref="IsRestartRequired"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsRestartRequiredProperty =
        AvaloniaProperty.Register<SettingsExpander, bool>(nameof(IsRestartRequired), defaultValue: false);

    /// <summary>
    /// Defines the <see cref="RestartCommand"/> property.
    /// </summary>
    public static readonly StyledProperty<ICommand?> RestartCommandProperty =
        AvaloniaProperty.Register<SettingsExpander, ICommand?>(nameof(RestartCommand), defaultValue: null);

    private Control? _actionPresenter;
    private Control? _footerPresenter;
    private Border? _headerBorder;
    private Control? _restartBanner;

    /// <summary>
    /// Gets or sets the right-aligned action content displayed in the expander header card.
    /// </summary>
    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    /// <summary>
    /// Gets or sets the data template used to display <see cref="Content"/>.
    /// </summary>
    public IDataTemplate? ContentTemplate
    {
        get => GetValue(ContentTemplateProperty);
        set => SetValue(ContentTemplateProperty, value);
    }

    /// <summary>
    /// Gets or sets the optional supporting description text of the expander header card.
    /// </summary>
    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>
    /// Gets or sets optional custom footer content displayed at the bottom of the header card.
    /// </summary>
    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    /// <summary>
    /// Gets or sets the data template for <see cref="Footer"/>.
    /// </summary>
    public IDataTemplate? FooterTemplate
    {
        get => GetValue(FooterTemplateProperty);
        set => SetValue(FooterTemplateProperty, value);
    }

    /// <summary>
    /// Gets or sets the primary title text of the expander header card.
    /// </summary>
    public string? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>
    /// Gets or sets the optional leading icon geometry of the expander header card.
    /// </summary>
    public StreamGeometry? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the child settings cards are expanded and visible.
    /// </summary>
    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the custom <see cref="Footer"/> section is visible.
    /// </summary>
    public bool IsFooterVisible
    {
        get => GetValue(IsFooterVisibleProperty);
        set => SetValue(IsFooterVisibleProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the built-in restart prompt banner is shown on the header card.
    /// </summary>
    public bool IsRestartRequired
    {
        get => GetValue(IsRestartRequiredProperty);
        set => SetValue(IsRestartRequiredProperty, value);
    }

    /// <summary>
    /// Gets or sets the command invoked when the user clicks the restart action in the banner.
    /// </summary>
    public ICommand? RestartCommand
    {
        get => GetValue(RestartCommandProperty);
        set => SetValue(RestartCommandProperty, value);
    }

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
    {
        recycleKey = null;
        return item is not Control;
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _headerBorder?.PointerPressed -= OnHeaderBorderPointerPressed;

        _headerBorder = e.NameScope.Find<Border>("PART_HeaderBorder");
        _actionPresenter = e.NameScope.Find<Control>("PART_ActionPresenter");
        _restartBanner = e.NameScope.Find<Control>("PART_RestartBanner");
        _footerPresenter = e.NameScope.Find<Control>("PART_FooterPresenter");

        _headerBorder?.PointerPressed += OnHeaderBorderPointerPressed;

        PseudoClasses.Set(":expanded", IsExpanded);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsExpandedProperty)
        {
            PseudoClasses.Set(":expanded", change.GetNewValue<bool>());
        }
    }

    private bool IsInsideInteractiveRegion(Visual source)
    {
        if (_actionPresenter is not null && _actionPresenter.IsVisualAncestorOf(source))
        {
            return true;
        }

        if (_restartBanner is not null && _restartBanner.IsVisualAncestorOf(source))
        {
            return true;
        }

        if (_footerPresenter is not null && _footerPresenter.IsVisualAncestorOf(source))
        {
            return true;
        }

        return false;
    }

    private void OnHeaderBorderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Handled || _headerBorder is null)
        {
            return;
        }

        if (!e.GetCurrentPoint(_headerBorder).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.Source is Visual sourceVisual && IsInsideInteractiveRegion(sourceVisual))
        {
            return;
        }

        IsExpanded = !IsExpanded;
        e.Handled = true;
    }
}
