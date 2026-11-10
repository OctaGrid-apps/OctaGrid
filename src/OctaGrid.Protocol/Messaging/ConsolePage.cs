namespace OctaGrid.Protocol.Messaging;

/// <summary>A slice of a printer's console log.</summary>
/// <param name="Lines">The lines, oldest first.</param>
/// <param name="LatestSequence">
/// The sequence of the newest line the agent has recorded for the printer, or 0 when there is none. When it is greater
/// than the last sequence in <paramref name="Lines"/>, more lines can be fetched after that one.
/// </param>
/// <param name="Truncated">
/// True when the page does not reach back as far as the client asked, because older lines were dropped to keep the log
/// bounded or because more were available than the requested limit.
/// </param>
/// <param name="StreamId">
/// Identifies the log these lines belong to. The agent starts a new log, with sequences from 1 again, when it
/// restarts. A client whose cursor came from a different id must discard what it has and ask again without a cursor.
/// <see cref="Guid.Empty"/> when the agent has no log for the printer.
/// </param>
public record ConsolePage(IReadOnlyList<ConsoleLine> Lines, long LatestSequence, bool Truncated, Guid StreamId);
