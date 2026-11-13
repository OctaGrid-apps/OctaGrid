namespace OctaGrid.Extensions.Models;

/// <summary>How serious a <see cref="PrinterCondition"/> or printer event is. Ordered from least to most serious.</summary>
public enum PrinterSeverity
{
    /// <summary>Informational: something happened or was resolved, and no action is needed.</summary>
    Info = 0,

    /// <summary>The printer works but needs attention, for example a filament warning.</summary>
    Warning = 1,

    /// <summary>The printer cannot do what it was asked, for example it is unreachable or has shut down.</summary>
    Error = 2
}
