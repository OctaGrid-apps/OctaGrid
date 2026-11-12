using OctaGrid.Extensions.Models;

namespace OctaGrid.Protocol.AgentLink.Notifications;

/// <summary>A printer's current state, sent whenever it changes.</summary>
/// <param name="Telemetry">The complete snapshot.</param>
public sealed record PrinterTelemetryReported(PrinterTelemetry Telemetry) : AgentNotification;
