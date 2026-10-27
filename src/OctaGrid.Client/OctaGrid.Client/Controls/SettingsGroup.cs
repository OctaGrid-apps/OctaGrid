using Avalonia;
using Avalonia.Controls;

namespace OctaGrid.Client.Controls;

/// <summary>
/// A Windows 11 Settings-style group container that displays a section header
/// and a vertical stack of <see cref="SettingsCard"/> or <see cref="SettingsExpander"/> items.
/// </summary>
public class SettingsGroup : ItemsControl
{
    /// <summary>
    /// Defines the <see cref="Description"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<SettingsGroup, string?>(nameof(Description), defaultValue: null);

    /// <summary>
    /// Defines the <see cref="Header"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> HeaderProperty =
        AvaloniaProperty.Register<SettingsGroup, string?>(nameof(Header), defaultValue: null);

    /// <summary>
    /// Gets or sets the optional supporting description text displayed below the group header.
    /// </summary>
    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>
    /// Gets or sets the group header text displayed above the settings cards.
    /// </summary>
    public string? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
    {
        recycleKey = null;
        return item is not Control;
    }
}
