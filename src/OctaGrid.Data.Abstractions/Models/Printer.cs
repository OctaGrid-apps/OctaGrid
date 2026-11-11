namespace OctaGrid.Data.Abstractions.Models;

public sealed class Printer : Entity
{
    public string Name { get; set; } = string.Empty;
    public Guid WorkspaceId { get; set; }
    public string? ReportedModel { get; set; }
    public bool Enabled { get; set; }
    public string ConnectorId { get; set; } = string.Empty;
    /// <summary>Values for the connector's non-secret settings (address, serial number, ...), by key.</summary>
    public Dictionary<string, string> Settings { get; set; } = [];
    /// <summary>
    /// The keys of the secret settings that have a saved value, readable without the encryption key. The values
    /// are in <see cref="ProtectedSecrets"/>.
    /// </summary>
    public List<string> SecretKeys { get; set; } = [];

    /// <summary>
    /// The secret settings (API keys, access codes, ...) as encrypted text, or null when there are none.
    /// The key is kept outside the database, so this column alone does not reveal them.
    /// </summary>
    public string? ProtectedSecrets { get; set; }

    // Navigation props
    public Workspace? Workspace { get; set; }

    public PrinterStatus? Status { get; set; }

    public ICollection<PrinterAgent> PrinterAgents { get; set; } = [];
}
