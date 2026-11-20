namespace OctaGrid.Client.Desktop.Services;

/// <summary>
/// The app's Microsoft Store product and the Store links derived from its Store ID.
/// </summary>
internal sealed record StoreProduct
{
    /// <summary>The Store's Downloads and updates page. Used when no Store ID is configured.</summary>
    public static readonly Uri UpdatesPageUri = new("ms-windows-store://downloadsandupdates");

    private StoreProduct(string id)
    {
        Id = id;
    }

    /// <summary>The Store ID.</summary>
    public string Id { get; }

    /// <summary>The product details page in the Store app, in the recommended <c>ProductId</c> form.</summary>
    public Uri ListingUri => new($"ms-windows-store://pdp/?ProductId={Id}");

    /// <summary>
    /// The product entry in the Store display catalog, which lists the published package versions. The catalog is not a
    /// documented API; it is queried only when <c>StoreContext</c> is unavailable, such as for sideloaded packages.
    /// </summary>
    public Uri CatalogUri => new($"https://displaycatalog.mp.microsoft.com/v7/products/{Id}?market=US&languages=en-US");

    /// <summary>
    /// Creates the product for <paramref name="productId"/>. Returns null when the ID is missing or contains anything
    /// other than ASCII letters and digits, because the ID is embedded in URIs.
    /// </summary>
    public static StoreProduct? From(string? productId)
    {
        var id = productId?.Trim();
        return string.IsNullOrEmpty(id) || !id.All(char.IsAsciiLetterOrDigit) ? null : new StoreProduct(id);
    }
}
