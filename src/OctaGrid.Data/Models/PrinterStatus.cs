namespace OctaGrid.Data.Models;

public sealed class PrinterStatus
{
    public Guid PrinterId { get; set; }
    public ConnectionState State { get; set; }
    public DateTimeOffset? ObservedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }

    // Navigation props
    public Printer? Printer { get; set; }
}
