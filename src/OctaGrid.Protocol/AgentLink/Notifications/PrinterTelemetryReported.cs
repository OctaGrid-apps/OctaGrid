using System.Text.Json.Serialization;
using OctaGrid.Extensions.Models;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Protocol.AgentLink.Notifications;

/// <summary>A printer's current state, sent whenever it changes.</summary>
/// <param name="Telemetry">The snapshot the printer's connector reported.</param>
/// <param name="Sample">
/// The history sample the agent stored from this snapshot, or null when it stored none: the snapshot carried no
/// readings, or came too soon after the previous sample. Clients add it to their copy of the history as it is.
/// </param>
public sealed record PrinterTelemetryReported(PrinterTelemetry Telemetry, TelemetrySample? Sample) : AgentNotification
{
    /// <inheritdoc />
    [JsonIgnore]
    public override AgentNotificationChannel Channel => AgentNotificationChannel.Telemetry;
}
