namespace OctaGrid.Client.ViewModels;

/// <summary>An entry of the connector dropdown on the list dashboard.</summary>
/// <param name="ConnectorId">The connector to show, or null for printers of every connector.</param>
/// <param name="DisplayName">Text shown in the dropdown.</param>
public sealed record ConnectorFilterOption(string? ConnectorId, string DisplayName)
{
    public static ConnectorFilterOption All { get; } = new(null, "All printers");
}
