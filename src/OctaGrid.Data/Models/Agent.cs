namespace OctaGrid.Data.Models;

public sealed class Agent : Entity
{
    public string Name { get; set; } = string.Empty;
    public Guid WorkspaceId { get; set; }
    public string? TokenPrefix { get; set; }
    public byte[] TokenHash { get; set; } = [];
    public string? PublicKey { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }

    // Its workspace and its printers are other aggregates, referred to by id (Printer.AgentId).
    public AgentStatus? Status { get; set; }
}
