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
public partial class SettingsCard : HeaderedContentControl
{
    /// <summary>
    /// Gets or sets the optional supporting description text displayed below the header.
    /// </summary>
    [GeneratedStyledProperty]
    public partial string? Description { get; set; }

    /// <summary>
    /// Gets or sets optional custom footer content displayed at the bottom of the card.
    /// </summary>
    [GeneratedStyledProperty]
    public partial object? Footer { get; set; }

    /// <summary>
    /// Gets or sets the data template for <see cref="Footer"/>.
    /// </summary>
    [GeneratedStyledProperty]
    public partial IDataTemplate? FooterTemplate { get; set; }

    /// <summary>
    /// Gets or sets the optional leading icon geometry.
    /// </summary>
    [GeneratedStyledProperty]
    public partial StreamGeometry? Icon { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the custom <see cref="Footer"/> section is visible.
    /// </summary>
    [GeneratedStyledProperty(DefaultValue = true)]
    public partial bool IsFooterVisible { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the built-in restart prompt banner is shown.
    /// </summary>
    [GeneratedStyledProperty]
    public partial bool IsRestartRequired { get; set; }

    /// <summary>
    /// Gets or sets the command invoked when the user clicks the restart action in the banner.
    /// </summary>
    [GeneratedStyledProperty]
    public partial ICommand? RestartCommand { get; set; }
}
