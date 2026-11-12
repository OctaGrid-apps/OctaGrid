namespace OctaGrid.Extensions.Models;

/// <summary>
/// A snapshot of one printer's current state. It is sent often, so it carries only what is true now; what
/// happened over time is a printer event. Connectors must not change a snapshot after sending it.
/// </summary>
public record PrinterTelemetry(Guid PrinterId)
{
    public double? NozzleTemperature { get; set; }
    public double? NozzleTargetTemperature { get; set; }
    public double? BedTemperature { get; set; }
    public double? BedTargetTemperature { get; set; }
    public UniversalPrinterState State { get; set; } = UniversalPrinterState.Offline;

    /// <summary>
    /// Everything that is wrong with the printer right now. Each snapshot holds the complete set, so a
    /// resolved problem disappears by being left out. Keep it to a handful of entries. While <see cref="State"/> is
    /// <see cref="UniversalPrinterState.Offline"/> the host treats a missing condition as unknown rather than resolved,
    /// because a connector that cannot reach the printer cannot know that the problem went away.
    /// </summary>
    public IReadOnlyList<PrinterCondition> Conditions { get; set; } = [];

    public PrintStatus PrintStatus { get; set; } = new PrintStatus();
}
