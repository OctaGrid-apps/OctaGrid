using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Extensions.Abstractions;
using OctaGrid.Extensions.Models;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Agent.Core.Events;

/// <summary>
/// Remembers what each printer wrote to its console, in a fixed-size log per printer, so a client that opens the console
/// later can see what it missed. Connectors write to it through an <see cref="IConsoleSink"/> and cannot grow it:
/// lines are cleaned up, cut to <see cref="PrinterConsoleLogOptions.MaxLineLength"/> and rate limited here.
/// <para>
/// The log lives in memory. It starts empty when the agent starts, and sequences then start again at 1.
/// </para>
/// </summary>
public sealed class PrinterConsoleLog
{
    private const string Ellipsis = "…";

    // Besides CR and LF, the separators that end a line in Unicode text (next line, line and paragraph separator).
    private static readonly char[] LineBreaks = ['\r', '\n', '\u0085', '\u2028', '\u2029'];

    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Journal> _journals = [];
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;

    public PrinterConsoleLog(
        TimeProvider? timeProvider = null,
        PrinterConsoleLogOptions? options = null,
        ILogger<PrinterConsoleLog>? logger = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<PrinterConsoleLog>.Instance;
        Options = options ?? new PrinterConsoleLogOptions();
    }

    /// <summary>
    /// Raised after a line is recorded, with the printer and log it belongs to, so clients can show it live. Raised on
    /// the thread that wrote the line, outside the log's lock; a handler that throws does not affect the writer. Handlers
    /// must return quickly, because they delay that connector.
    /// </summary>
    public event Action<PrinterConsoleLineRecorded>? Recorded;

    /// <summary>The limits this log enforces.</summary>
    public PrinterConsoleLogOptions Options { get; }

    /// <summary>The sink that records what a printer's connector writes. One per printer session.</summary>
    public IConsoleSink CreateSink(Guid printerId) => new Sink(this, printerId);

    /// <summary>
    /// Returns part of a printer's console log, oldest first.
    /// </summary>
    /// <param name="printerId">The printer.</param>
    /// <param name="afterSequence">
    /// The sequence of the last line the caller has, to get what came after it. Null returns the newest
    /// <paramref name="limit"/> lines. A cursor only fits the log it came from: compare <see cref="ConsolePage.StreamId"/>
    /// with the one the cursor was taken from, and when they differ, ask again without a cursor.
    /// </param>
    /// <param name="limit">The most lines to return; limited to the log's capacity.</param>
    public ConsolePage Get(Guid printerId, long? afterSequence, int limit)
    {
        limit = Math.Clamp(limit, 1, Options.Capacity);

        lock (_gate)
        {
            if (!_journals.TryGetValue(printerId, out var journal))
            {
                return new ConsolePage([], 0, false, Guid.Empty);
            }

            if (journal.Lines.Count == 0)
            {
                return new ConsolePage([], journal.LastSequence, false, journal.StreamId);
            }

            var oldest = journal.Lines.Peek().Sequence;
            if (afterSequence is not { } after)
            {
                var newest = journal.Lines.Skip(Math.Max(0, journal.Lines.Count - limit)).ToList();
                return new ConsolePage(newest, journal.LastSequence, newest[0].Sequence > 1, journal.StreamId);
            }

            var following = journal.Lines.Where(l => l.Sequence > after).Take(limit).ToList();
            return new ConsolePage(following, journal.LastSequence, after + 1 < oldest, journal.StreamId);
        }
    }

    /// <summary>Forgets the log of every printer that is not in <paramref name="printerIds"/>.</summary>
    public void Retain(IReadOnlyCollection<Guid> printerIds)
    {
        var keep = printerIds.ToHashSet();
        lock (_gate)
        {
            foreach (var id in _journals.Keys.Where(id => !keep.Contains(id)).ToList())
            {
                _journals.Remove(id);
            }
        }
    }

    /// <summary>
    /// Records a command the user sent, so it is shown before what the printer answers. It is not held to the rate limit:
    /// that limit protects the log from a printer that floods it, and the user typing a command is not a flood. Without
    /// this, an emergency stop sent while the printer spams the console would leave no trace of what was sent.
    /// </summary>
    internal void Echo(Guid printerId, string text) => Write(printerId, text, rateLimited: false);

    /// <summary>Records what a printer wrote. Several lines in one text become several entries.</summary>
    internal void Write(Guid printerId, string? text, bool rateLimited = true)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        // Extensions are not trusted, so the work is bounded before anything is done: text beyond what one write may carry is
        // never looked at, however much was handed over, and is only counted as skipped.
        var truncated = text.Length > Options.MaxWriteLength;
        var accepted = truncated ? text[..Options.MaxWriteLength] : text;

        var recorded = new List<ConsoleLine>();
        Guid streamId;
        lock (_gate)
        {
            var journal = GetJournal(printerId);
            streamId = journal.StreamId;
            foreach (var raw in accepted.Split(LineBreaks, StringSplitOptions.RemoveEmptyEntries))
            {
                Append(journal, raw, recorded, rateLimited);
            }

            if (truncated)
            {
                journal.Skipped++;
            }
        }

        Raise(printerId, streamId, recorded);
    }

    // The text of one line as it is kept, or null when nothing is left of it: control characters, line separators and
    // escapes mean nothing in a list, and a line is cut to its limit.
    private string? Prepare(string raw)
    {
        var limit = Options.MaxLineLength;
        var builder = new StringBuilder(Math.Min(raw.Length, limit + 1));
        foreach (var c in raw)
        {
            builder.Append(ConsoleCommandRules.IsLineBreakOrControl(c) ? ' ' : c);
            if (builder.Length > limit)
            {
                break;
            }
        }

        // Whether the line is too long is decided before trailing spaces are trimmed: trimming first could bring a line that
        // was cut back under the limit, and it would be kept cut off with nothing to say so.
        if (builder.Length > limit)
        {
            var kept = builder.ToString(0, limit - Ellipsis.Length).TrimEnd();
            return kept.Length == 0 ? null : kept + Ellipsis;
        }

        var line = builder.ToString().TrimEnd();
        return line.Length == 0 ? null : line;
    }

    // Callers hold the lock.
    private void Add(Journal journal, DateTimeOffset now, string text, List<ConsoleLine> recorded)
    {
        var line = new ConsoleLine(++journal.LastSequence, now, text);
        journal.Lines.Enqueue(line);
        recorded.Add(line);

        while (journal.Lines.Count > Options.Capacity)
        {
            journal.Lines.Dequeue();
        }
    }

    // Callers hold the lock.
    private void Append(Journal journal, string raw, List<ConsoleLine> recorded, bool rateLimited)
    {
        var now = _timeProvider.GetUtcNow();
        journal.Tokens = Math.Min(Options.Burst, journal.Tokens + (now - journal.RefilledAt).TotalSeconds * Options.LinesPerSecond);
        journal.RefilledAt = now;

        // Checked before the line is cleaned: a line that is going to be skipped costs nothing but the count.
        if (rateLimited && journal.Tokens < 1)
        {
            journal.Skipped++;
            return;
        }

        if (Prepare(raw) is not { } text)
        {
            return;
        }

        if (rateLimited)
        {
            journal.Tokens--;
        }

        if (journal.Skipped > 0)
        {
            // Said once the flood is over, so the reader knows the list has a hole and how big it is.
            Add(journal, now, $"… {journal.Skipped} lines skipped", recorded);
            journal.Skipped = 0;
        }

        Add(journal, now, text, recorded);
    }

    private Journal GetJournal(Guid printerId)
    {
        if (!_journals.TryGetValue(printerId, out var journal))
        {
            journal = new Journal(Options.Burst, _timeProvider.GetUtcNow());
            _journals[printerId] = journal;
        }

        return journal;
    }

    private void Raise(Guid printerId, Guid streamId, List<ConsoleLine> recorded)
    {
        if (recorded.Count == 0 || Recorded is not { } handlers)
        {
            return;
        }

        foreach (var line in recorded)
        {
            var notice = new PrinterConsoleLineRecorded(printerId, streamId, line);
            foreach (var handler in handlers.GetInvocationList().Cast<Action<PrinterConsoleLineRecorded>>())
            {
                try
                {
                    handler(notice);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "A handler of console line {Sequence} for printer {PrinterId} failed", line.Sequence, printerId);
                }
            }
        }
    }

    private sealed class Sink : IConsoleSink
    {
        private readonly PrinterConsoleLog _log;
        private readonly Guid _printerId;

        public Sink(PrinterConsoleLog log, Guid printerId)
        {
            _log = log;
            _printerId = printerId;
        }

        public void WriteLine(string text) => _log.Write(_printerId, text);
    }

    private sealed class Journal
    {
        public Journal(double tokens, DateTimeOffset now)
        {
            Tokens = tokens;
            RefilledAt = now;
        }

        public long LastSequence { get; set; }

        public Queue<ConsoleLine> Lines { get; } = new();

        public DateTimeOffset RefilledAt { get; set; }

        public int Skipped { get; set; }

        /// <summary>Changes whenever the printer's log starts over, so clients can tell two logs apart.</summary>
        public Guid StreamId { get; } = Guid.NewGuid();

        public double Tokens { get; set; }
    }
}
