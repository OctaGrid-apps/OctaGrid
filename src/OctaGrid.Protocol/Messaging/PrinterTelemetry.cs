using System.ComponentModel.DataAnnotations;

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

public record PrinterStateMessage(string Message);

public record PrintStatus
{
    public string? FileName { get; set; }
    public TimeOnly PrintTime { get; set; }
    public TimeOnly PrintTimeTotal { get; set; }
    public int? LayersCount { get; set; }
    public int? CurrentLayer { get; set; }
    public double? Progress { get; set; }

    // The slicer's predicted total duration for this file (server.files.metadata's
    // "estimated_time"), fetched on demand — not pushed by notify_status_update. Distinct from
    // PrintTimeTotal, which is the actual elapsed duration reported by print_stats.
    public TimeOnly? EstimatedPrintTime { get; set; }
}

public enum UniversalPrinterState
{
    [Display(Name = "Offline", Description = "Printer is disconnected or unreachable")]
    Offline = 0,

    [Display(Name = "Idle", Description = "Connected and ready to receive jobs")]
    Idle = 1,

    [Display(Name = "Preparing", Description = "Heating, homing, or running bed calibration")]
    Preparing = 2,

    [Display(Name = "Printing", Description = "Active print in progress")]
    Printing = 3,

    [Display(Name = "Paused", Description = "Print paused by user or filament runout")]
    Paused = 4,

    [Display(Name = "Cancelled", Description = "Job was cancelled before it finished; printer is ready")]
    Cancelled = 5,

    [Display(Name = "Completed", Description = "Job finished successfully; printer is ready")]
    Completed = 6,

    // The PRINTER is faulted (e.g. Klipper shutdown). A job that merely ended badly is Failed.
    [Display(Name = "Error", Description = "Thermal runaway, min/max temp fault, or crash")]
    Error = 7,

    // The JOB ended with an error (e.g. a G-code error) but the printer itself is fine.
    [Display(Name = "Failed", Description = "Job ended with an error; printer is ready")]
    Failed = 8
}
