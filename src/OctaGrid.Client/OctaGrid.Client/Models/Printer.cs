namespace OctaGrid.Client.Models;

public record Printer()
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ConnectorId { get; set; } = string.Empty;

    /// <summary>Values for the connector's non-secret settings, by key.</summary>
    public Dictionary<string, string> Settings { get; set; } = [];

    /// <summary>
    /// Secret settings the user entered, by key. Saved secrets cannot be read back, so this holds only what
    /// was typed in the form; see <see cref="SavedSecretKeys"/> for the ones that already have a value.
    /// </summary>
    public Dictionary<string, string> Secrets { get; set; } = [];

    /// <summary>The keys of the secret settings that have a saved value.</summary>
    public IReadOnlyList<string> SavedSecretKeys { get; set; } = [];

    public bool Enabled { get; set; } = true;
}
