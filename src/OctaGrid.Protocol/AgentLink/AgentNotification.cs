using System.Text.Json.Serialization;
using OctaGrid.Protocol.AgentLink.Notifications;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Protocol.AgentLink;

/// <summary>
/// Something an agent reports on its own, without being asked: telemetry, a recorded event, a console line.
/// The names below are the wire names: never change or reuse one.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(PrinterTelemetryReported), "telemetry")]
[JsonDerivedType(typeof(PrinterEventRecorded), "printer-event")]
[JsonDerivedType(typeof(PrinterConsoleLineRecorded), "console-line")]
[JsonDerivedType(typeof(ConnectorsChanged), "connectors-changed")]
[JsonDerivedType(typeof(PrinterStateChanged), "printer-state")]
public abstract record AgentNotification
{
    /// <summary>The stream the notification travels on. Not sent: it follows from the notification's type.</summary>
    [JsonIgnore]
    public virtual AgentNotificationChannel Channel => AgentNotificationChannel.Events;
}
