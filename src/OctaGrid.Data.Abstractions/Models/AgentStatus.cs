namespace OctaGrid.Data.Abstractions.Models;

public sealed class AgentStatus
{
    public Guid AgentId { get; set; }
    public AgentState State { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? LastConnectedAt { get; set; }

    // Navigation props
    public Agent? Agent { get; set; }
}
