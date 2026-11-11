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
    /// <summary>Values for the connector's secret settings (API keys, access codes, ...), by key.</summary>
    // ToDo: stored as-is for now; encrypt before they leave the local machine.
    /// <summary>
    /// The keys of the secret settings that have a saved value. The values themselves are kept in the
    /// secret store, never in the database.
    /// </summary>
    public List<string> SecretKeys { get; set; } = [];

    // Navigation props
    public Workspace? Workspace { get; set; }

    public PrinterStatus? Status { get; set; }

    public ICollection<PrinterAgent> PrinterAgents { get; set; } = [];
}
