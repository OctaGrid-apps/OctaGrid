using Avalonia;
using Avalonia.Controls;

namespace OctaGrid.Client.Controls;

/// <summary>
/// A Windows 11 Settings-style group container that displays a section header
/// and a vertical stack of <see cref="SettingsCard"/> or <see cref="SettingsExpander"/> items.
/// </summary>
public partial class SettingsGroup : ItemsControl
{
    /// <summary>
    /// Gets or sets the optional supporting description text displayed below the group header.
    /// </summary>
    [GeneratedStyledProperty]
    public partial string? Description { get; set; }

    /// <summary>
    /// Gets or sets the group header text displayed above the settings cards.
    /// </summary>
    [GeneratedStyledProperty]
    public partial string? Header { get; set; }

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
    {
        recycleKey = null;
        return item is not Control;
    }
}
