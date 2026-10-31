namespace OctaGrid.Data.Abstractions.Models;

public sealed class Printer : Entity
{
    public string Name { get; set; } = string.Empty;
    public Guid WorkspaceId { get; set; }
    public string? ReportedModel { get; set; }
    public bool Enabled { get; set; }
    public PrinterProtocol Protocol { get; set; }
    public string EndpointAddress { get; set; } = string.Empty;
    public string? EncryptedApiKey { get; set; }

    // Navigation props
    public Workspace? Workspace { get; set; }

    public PrinterStatus? Status { get; set; }

    public ICollection<PrinterAgent> PrinterAgents { get; set; } = [];
}
