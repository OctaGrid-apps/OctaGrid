namespace OctaGrid.Data.Models;

public sealed class PrinterAgent : Entity
{
    public Guid PrinterId { get; set; }
    public Guid AgentId { get; set; }

    // Navigation props
    public Printer? Printer { get; set; }

    public Agent? Agent { get; set; }
}
