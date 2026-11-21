using OctaGrid.Extensions.Models;

namespace OctaGrid.Protocol.AgentLink.Notifications;

/// <summary>
/// A printer's <see cref="UniversalPrinterState"/> changed. Sent for every printer the agent runs, whether or not
/// anyone watches its telemetry, so the backend can follow print jobs without the telemetry stream.
/// </summary>
/// <param name="PrinterId">The printer whose state changed.</param>
/// <param name="Previous">
/// The state the agent saw before, or null for the first state it saw since it started or since it got the printer.
/// </param>
/// <param name="Current">The state the printer is in now.</param>
/// <param name="FileName">The file the printer reported as its current print, if any.</param>
/// <param name="OccurredAt">When the agent saw the change.</param>
public sealed record PrinterStateChanged(
    Guid PrinterId,
    UniversalPrinterState? Previous,
    UniversalPrinterState Current,
    string? FileName,
    DateTimeOffset OccurredAt) : AgentNotification;
