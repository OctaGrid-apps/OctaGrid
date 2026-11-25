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
public partial class SettingsExpander : ItemsControl
{
    private Control? _actionPresenter;
    private Control? _footerPresenter;
    private Border? _headerBorder;
    private Control? _restartBanner;

    /// <summary>
    /// Gets or sets the right-aligned action content displayed in the expander header card.
    /// </summary>
    [GeneratedStyledProperty]
    public partial object? Content { get; set; }

    /// <summary>
    /// Gets or sets the data template used to display <see cref="Content"/>.
    /// </summary>
    [GeneratedStyledProperty]
    public partial IDataTemplate? ContentTemplate { get; set; }

    /// <summary>
    /// Gets or sets the optional supporting description text of the expander header card.
    /// </summary>
    [GeneratedStyledProperty]
    public partial string? Description { get; set; }

    /// <summary>
    /// Gets or sets optional custom footer content displayed at the bottom of the header card.
    /// </summary>
    [GeneratedStyledProperty]
    public partial object? Footer { get; set; }

    /// <summary>
    /// Gets or sets the data template for <see cref="Footer"/>.
    /// </summary>
    [GeneratedStyledProperty]
    public partial IDataTemplate? FooterTemplate { get; set; }

    /// <summary>
    /// Gets or sets the primary title text of the expander header card.
    /// </summary>
    [GeneratedStyledProperty]
    public partial string? Header { get; set; }

    /// <summary>
    /// Gets or sets the optional leading icon geometry of the expander header card.
    /// </summary>
    [GeneratedStyledProperty]
    public partial StreamGeometry? Icon { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the child settings cards are expanded and visible.
    /// </summary>
    [GeneratedStyledProperty(DefaultBindingMode = BindingMode.TwoWay)]
    public partial bool IsExpanded { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the custom <see cref="Footer"/> section is visible.
    /// </summary>
    [GeneratedStyledProperty(DefaultValue = true)]
    public partial bool IsFooterVisible { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the built-in restart prompt banner is shown on the header card.
    /// </summary>
    [GeneratedStyledProperty]
    public partial bool IsRestartRequired { get; set; }

    /// <summary>
    /// Gets or sets the command invoked when the user clicks the restart action in the banner.
    /// </summary>
    [GeneratedStyledProperty]
    public partial ICommand? RestartCommand { get; set; }

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
