using System.ComponentModel.DataAnnotations;

namespace OctaGrid.Protocol.Messaging;

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
