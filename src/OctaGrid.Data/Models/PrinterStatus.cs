using OctaGrid.Extensions.Models;

namespace OctaGrid.Data.Models;

/// <summary>
/// The last state the printer's agent reported, stored so the backend knows it without live telemetry.
/// Part of the <see cref="Printer"/> aggregate; changed only through <see cref="Printer.ObserveState"/>.
/// </summary>
public sealed class PrinterStatus
{
    public Guid PrinterId { get; private set; }

    /// <summary>The last reported state; null until the agent reports one.</summary>
    public UniversalPrinterState? State { get; private set; }

    /// <summary>When the agent saw <see cref="State"/>.</summary>
    public DateTimeOffset? ObservedAt { get; private set; }

    /// <summary>When the printer was last reported in a state other than offline.</summary>
    public DateTimeOffset? LastSeenAt { get; private set; }

    // Navigation props
    public Printer? Printer { get; private set; }

    /// <returns>True when the state was recorded; false when it is older than the stored one.</returns>
    internal bool Observe(UniversalPrinterState state, DateTimeOffset at)
    {
        if (ObservedAt is { } last && at < last)
        {
            return false;
        }

        State = state;
        ObservedAt = at;
        if (state != UniversalPrinterState.Offline)
        {
            LastSeenAt = at;
        }

        return true;
    }
}
