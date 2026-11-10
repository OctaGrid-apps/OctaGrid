namespace OctaGrid.Protocol.Messaging;

/// <summary>Sent to clients when the agent records a new <see cref="ConsoleLine"/>, so an open console can show it live.</summary>
/// <param name="PrinterId">The printer the line belongs to.</param>
/// <param name="StreamId">The log the line was added to; see <see cref="ConsolePage.StreamId"/>.</param>
/// <param name="Line">The line, with the sequence the agent gave it.</param>
public record PrinterConsoleLineRecorded(Guid PrinterId, Guid StreamId, ConsoleLine Line);
