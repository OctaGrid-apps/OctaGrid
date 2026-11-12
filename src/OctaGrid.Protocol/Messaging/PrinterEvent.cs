using OctaGrid.Extensions.Models;

namespace OctaGrid.Protocol.Messaging;

/// <summary>
/// Something that happened to a printer, such as a state change or a <see cref="PrinterCondition"/> appearing.
/// The agent keeps a bounded history of these per printer, so a client that connects later can see
/// what it missed.
/// </summary>
/// <param name="Sequence">
/// Position in the printer's history, starting at 1 and increasing by 1 for every recorded event. Assigned by the
/// agent; clients use it as a cursor to ask for what came after the last event they saw. The history is not
/// persisted, so sequences start again at 1 after an agent restart; <see cref="PrinterEventPage.HistoryId"/> tells
/// a client that this happened.
/// </param>
/// <param name="Timestamp">When the agent recorded the event.</param>
/// <param name="Severity">How serious the event is.</param>
/// <param name="Code">A stable, machine-readable identifier such as <c>state.changed</c>.</param>
/// <param name="Message">A short sentence for the user.</param>
public record PrinterEvent(long Sequence, DateTimeOffset Timestamp, PrinterSeverity Severity, string Code, string Message);
