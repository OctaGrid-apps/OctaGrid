using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Media;

namespace OctaGrid.Client.Controls;

/// <summary>
/// A Windows 11 Settings-style card control with a header (string or any control),
/// optional description, optional leading icon, right-aligned action content,
/// built-in restart prompt, and optional custom footer content.
/// </summary>
public class SettingsCard : HeaderedContentControl
{
    /// <summary>
    /// Defines the <see cref="Description"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<SettingsCard, string?>(nameof(Description), defaultValue: null);

    /// <summary>
    /// Defines the <see cref="Footer"/> property.
    /// </summary>
    public static readonly StyledProperty<object?> FooterProperty =
        AvaloniaProperty.Register<SettingsCard, object?>(nameof(Footer), defaultValue: null);

    /// <summary>
    /// Defines the <see cref="FooterTemplate"/> property.
    /// </summary>
    public static readonly StyledProperty<IDataTemplate?> FooterTemplateProperty =
        AvaloniaProperty.Register<SettingsCard, IDataTemplate?>(nameof(FooterTemplate), defaultValue: null);

    /// <summary>
    /// Defines the <see cref="Icon"/> property.
    /// </summary>
    public static readonly StyledProperty<StreamGeometry?> IconProperty =
        AvaloniaProperty.Register<SettingsCard, StreamGeometry?>(nameof(Icon), defaultValue: null);

    /// <summary>
    /// Defines the <see cref="IsFooterVisible"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsFooterVisibleProperty =
        AvaloniaProperty.Register<SettingsCard, bool>(nameof(IsFooterVisible), defaultValue: true);

    /// <summary>
    /// Defines the <see cref="IsRestartRequired"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsRestartRequiredProperty =
        AvaloniaProperty.Register<SettingsCard, bool>(nameof(IsRestartRequired), defaultValue: false);

    /// <summary>
    /// Defines the <see cref="RestartCommand"/> property.
    /// </summary>
    public static readonly StyledProperty<ICommand?> RestartCommandProperty =
        AvaloniaProperty.Register<SettingsCard, ICommand?>(nameof(RestartCommand), defaultValue: null);

    /// <summary>
    /// Gets or sets the optional supporting description text displayed below the header.
    /// </summary>
    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>
    /// Gets or sets optional custom footer content displayed at the bottom of the card.
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
    /// Gets or sets the optional leading icon geometry.
    /// </summary>
    public StreamGeometry? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
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
    /// Gets or sets a value indicating whether the built-in restart prompt banner is shown.
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
}
