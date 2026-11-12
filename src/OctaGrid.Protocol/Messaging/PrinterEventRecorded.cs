using OctaGrid.Protocol.AgentLink;

namespace OctaGrid.Protocol.Messaging;

/// <summary>Sent to clients when the agent records a new <see cref="PrinterEvent"/>, so an open history can show it live.</summary>
/// <param name="PrinterId">The printer the event belongs to.</param>
/// <param name="HistoryId">The history the event was added to; see <see cref="PrinterEventPage.HistoryId"/>.</param>
/// <param name="Event">The event, with the sequence the agent gave it.</param>
public record PrinterEventRecorded(Guid PrinterId, Guid HistoryId, PrinterEvent Event) : AgentNotification;
