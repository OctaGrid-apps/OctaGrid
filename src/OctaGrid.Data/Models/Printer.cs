namespace OctaGrid.Data.Models;

public sealed class Printer : Entity
{
    public string Name { get; set; } = string.Empty;
    public Guid WorkspaceId { get; set; }
    /// <summary>
    /// The agent that connects to the printer, or null when none is assigned yet. A printer has one agent at a
    /// time: two agents connecting to the same printer would fight over it.
    /// </summary>
    public Guid? AgentId { get; set; }
    public string? ReportedModel { get; set; }
    public bool Enabled { get; set; }
    public string ConnectorId { get; set; } = string.Empty;
    /// <summary>Values for the connector's non-secret settings (address, serial number, ...), by key.</summary>
    public Dictionary<string, string> Settings { get; set; } = [];
    /// <summary>
    /// The secret settings (API keys, access codes, ...) by key, as encrypted text, or null when there are none.
    /// The key is kept outside the database, so this column alone does not reveal them.
    /// </summary>
    public string? ProtectedSecrets { get; set; }

    // Navigation props
    public Workspace? Workspace { get; set; }

    public Agent? Agent { get; set; }

    public PrinterStatus? Status { get; set; }
}
