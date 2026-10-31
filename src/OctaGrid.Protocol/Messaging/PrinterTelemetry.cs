namespace OctaGrid.Protocol.Messaging;

public record PrinterTelemetry(Guid PrinterId)
{
    public double? NozzleTemperature { get; set; }
    public double? NozzleTargetTemperature { get; set; }
    public double? BedTemperature { get; set; }
    public double? BedTargetTemperature { get; set; }
    public UniversalPrinterState State { get; set; } = UniversalPrinterState.Offline;
    public Stack<PrinterStateMessage> StateMessages { get; set; } = [];
    public PrintStatus PrintStatus { get; set; } = new PrintStatus();
}
