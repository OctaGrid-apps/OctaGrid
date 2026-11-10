namespace OctaGrid.Protocol.Messaging;

/// <summary>A slice of a printer's event history.</summary>
/// <param name="Events">The events, oldest first.</param>
/// <param name="LatestSequence">
/// The sequence of the newest event the agent has recorded for the printer, or 0 when there is none. When it is
/// greater than the last sequence in <paramref name="Events"/>, more events can be fetched after that one.
/// </param>
/// <param name="Truncated">
/// True when the page does not reach back as far as the client asked, so it cannot show everything that happened.
/// With a cursor: events after it were dropped to keep the history bounded. Without one: older events exist that are
/// not in the page, because they were dropped or because more were available than the requested limit.
/// </param>
/// <param name="HistoryId">
/// Identifies the history these events belong to. The agent starts a new history, with sequences from 1 again, when
/// it restarts or forgets the printer. A client whose cursor came from a different id must discard what it has and
/// ask again without a cursor; comparing sequences alone cannot tell, because the new history may already be longer.
/// <see cref="Guid.Empty"/> when the agent has no history for the printer.
/// </param>
public record PrinterEventPage(IReadOnlyList<PrinterEvent> Events, long LatestSequence, bool Truncated, Guid HistoryId);
