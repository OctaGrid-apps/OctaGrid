using OctaGrid.Extensions.Models;

namespace OctaGrid.Protocol.Messaging;

/// <summary>
/// One line the printer wrote to its console, for example a G-code response such as <c>ok</c> or a temperature report.
/// The agent keeps a bounded log of these per printer; it is a log for the user to read, not printer state, so it is
/// kept apart from <see cref="PrinterTelemetry"/>.
/// </summary>
/// <param name="Sequence">
/// Position in the printer's console log, starting at 1 and increasing by 1 for every recorded line. Assigned by the
/// agent; clients use it as a cursor to ask for what came after the last line they saw. The log is not persisted, so
/// sequences start again at 1 after an agent restart; <see cref="ConsolePage.StreamId"/> tells a client that this happened.
/// </param>
/// <param name="Timestamp">When the agent recorded the line.</param>
/// <param name="Text">
/// The text of the line, without a line ending. The agent splits multi-line output into separate lines and shortens
/// very long ones, so a client can show it as it is.
/// </param>
public record ConsoleLine(long Sequence, DateTimeOffset Timestamp, string Text);
