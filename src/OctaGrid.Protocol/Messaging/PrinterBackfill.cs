using OctaGrid.Extensions.Models;

namespace OctaGrid.Protocol.Messaging;

/// <summary>
/// What a client needs to start showing a printer: its current state and its recent history. Live telemetry carries on
/// from there.
/// </summary>
/// <param name="PrinterId">The printer.</param>
/// <param name="Current">
/// The last snapshot the printer reported, or an <see cref="UniversalPrinterState.Offline"/> one when it has reported
/// nothing yet.
/// </param>
/// <param name="History">The stored samples, oldest first; only those after the time asked for, when one was given.</param>
public sealed record PrinterBackfill(Guid PrinterId, PrinterTelemetry Current, IReadOnlyList<TelemetrySample> History);
