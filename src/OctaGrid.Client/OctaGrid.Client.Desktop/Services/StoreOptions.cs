namespace OctaGrid.Client.Desktop.Services;

/// <summary>
/// Microsoft Store settings, bound from the <c>Store</c> section of <c>appsettings.json</c>.
/// </summary>
public sealed class StoreOptions
{
    /// <summary>The configuration section the options are bound from.</summary>
    public const string SectionName = "Store";

    /// <summary>
    /// The Store ID of the app, as shown in Partner Center. Release builds supply it; it is empty in source control.
    /// </summary>
    public string? ProductId { get; set; }
}
