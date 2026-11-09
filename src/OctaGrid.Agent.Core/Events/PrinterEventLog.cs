using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Agent.Core.Events;

/// <summary>
/// Remembers what happened to each printer, in a fixed-size history per printer, so a client that connects
/// later can see what it missed. The host owns this history: connectors only report state, and the log
/// derives events from the changes (see <see cref="Observe"/>), which means an extension cannot grow it.
/// <para>
/// The history lives in memory. It starts empty when the agent starts, and sequences then start again at 1.
/// </para>
/// </summary>
public sealed class PrinterEventLog
{
    private const string Ellipsis = "…";

    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Journal> _journals = [];
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;

    public PrinterEventLog(
        TimeProvider? timeProvider = null,
        PrinterEventLogOptions? options = null,
        ILogger<PrinterEventLog>? logger = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<PrinterEventLog>.Instance;
        Options = options ?? new PrinterEventLogOptions();
    }

    /// <summary>
    /// Raised after an event is recorded, with the printer and history it belongs to, so clients can show it live.
    /// Raised on the thread that reported the state, outside the log's lock; a handler that throws does not affect the
    /// reporter. Handlers must return quickly, because they delay that connector: one that has to do slow work (send
    /// over a network) should queue the notice and return.
    /// </summary>
    public event Action<PrinterEventRecorded>? Recorded;

    /// <summary>The limits this log enforces.</summary>
    public PrinterEventLogOptions Options { get; }

    /// <summary>
    /// Returns part of a printer's history, oldest first.
    /// </summary>
    /// <param name="printerId">The printer.</param>
    /// <param name="afterSequence">
    /// The sequence of the last event the caller has, to get what came after it. Null returns the newest
    /// <paramref name="limit"/> events, and then <see cref="PrinterEventPage.Truncated"/> is true when older events
    /// exist that are not in the page. A cursor only fits the history it came from: compare
    /// <see cref="PrinterEventPage.HistoryId"/> with the one the cursor was taken from, and when they differ, ask
    /// again without a cursor.
    /// </param>
    /// <param name="limit">The most events to return; limited to the log's capacity.</param>
    public PrinterEventPage Get(Guid printerId, long? afterSequence, int limit)
    {
        limit = Math.Clamp(limit, 1, Options.Capacity);

        lock (_gate)
        {
            if (!_journals.TryGetValue(printerId, out var journal))
            {
                return new PrinterEventPage([], 0, false, Guid.Empty);
            }

            if (journal.Events.Count == 0)
            {
                return new PrinterEventPage([], journal.LastSequence, false, journal.HistoryId);
            }

            var oldest = journal.Events.Peek().Sequence;
            if (afterSequence is not { } after)
            {
                var newest = journal.Events.Skip(Math.Max(0, journal.Events.Count - limit)).ToList();
                return new PrinterEventPage(newest, journal.LastSequence, newest[0].Sequence > 1, journal.HistoryId);
            }

            var following = journal.Events.Where(e => e.Sequence > after).Take(limit).ToList();
            return new PrinterEventPage(following, journal.LastSequence, oldest > after + 1, journal.HistoryId);
        }
    }

    /// <summary>
    /// Returns the snapshot with its conditions held to this log's limits: at most <see cref="PrinterEventLogOptions.MaxConditions"/>
    /// of them (the most serious kept), each with a trimmed, length-limited code and message, and one per code.
    /// Entries without text, and a missing list, are dropped. Returns the same instance when nothing needs changing.
    /// This is the single place the limits are applied, so the events recorded and the state sent on always agree.
    /// </summary>
    public PrinterTelemetry Sanitize(PrinterTelemetry telemetry)
    {
        if (IsClean(telemetry.Conditions))
        {
            return telemetry;
        }

        var cleaned = new List<PrinterCondition>();
        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var condition in (telemetry.Conditions ?? []).Where(IsUsable).OrderByDescending(c => c.Severity))
        {
            var code = Limit(condition.Code.Trim(), Options.MaxCodeLength);
            if (!codes.Add(code))
            {
                continue;
            }

            cleaned.Add(condition with { Code = code, Message = Limit(condition.Message.Trim(), Options.MaxMessageLength) });
            if (cleaned.Count == Options.MaxConditions)
            {
                break;
            }
        }

        return telemetry with { Conditions = cleaned };
    }

    /// <summary>
    /// Compares a snapshot with the previous one for the same printer and records what changed: a new
    /// state, a condition that appeared (or changed), and one that is gone.
    /// <para>
    /// While the state is <see cref="UniversalPrinterState.Offline"/> a condition that is missing is unknown, not
    /// resolved: the connector cannot see the printer, so nothing is recorded as resolved until the printer reports again.
    /// </para>
    /// </summary>
    public void Observe(PrinterTelemetry telemetry)
    {
        var clean = Sanitize(telemetry);
        var offline = clean.State == UniversalPrinterState.Offline;
        List<PrinterEvent> recorded;
        Guid historyId;

        lock (_gate)
        {
            var journal = GetJournal(clean.PrinterId);
            historyId = journal.HistoryId;

            // Nearly every snapshot only moves a temperature, so say "nothing to record" without allocating.
            if (clean.State == journal.State && Covers(journal.Conditions, clean.Conditions, exact: !offline))
            {
                return;
            }

            recorded = [];
            var current = clean.Conditions.ToDictionary(c => c.Code, StringComparer.Ordinal);

            if (clean.State != journal.State)
            {
                RecordLocked(journal, recorded, StateSeverity(clean.State), "state.changed", $"State changed to {clean.State}.");
            }

            foreach (var condition in clean.Conditions)
            {
                if (!journal.Conditions.TryGetValue(condition.Code, out var before) || before != condition)
                {
                    RecordLocked(journal, recorded, condition.Severity, condition.Code, condition.Message);
                }
            }

            if (!offline)
            {
                // Not rate-limited: a fault that was recorded must not look open because its end was dropped.
                foreach (var gone in journal.Conditions.Values.Where(c => !current.ContainsKey(c.Code)))
                {
                    RecordLocked(journal, recorded, PrinterSeverity.Info, gone.Code, $"Resolved: {gone.Message}", rateLimited: false);
                }

                journal.Conditions = current;
            }
            else
            {
                foreach (var condition in current.Values)
                {
                    journal.Conditions[condition.Code] = condition;
                }

                // What is remembered while the printer cannot be seen stays bounded, whatever the connector sends:
                // keep what this snapshot reported, then the most serious of the rest.
                if (journal.Conditions.Count > Options.MaxConditions)
                {
                    journal.Conditions = journal.Conditions.Values
                        .OrderByDescending(c => current.ContainsKey(c.Code))
                        .ThenByDescending(c => c.Severity)
                        .Take(Options.MaxConditions)
                        .ToDictionary(c => c.Code, StringComparer.Ordinal);
                }
            }

            journal.State = clean.State;
        }

        Raise(clean.PrinterId, historyId, recorded);
    }

    /// <summary>
    /// Records one event. Returns null when it was left out: it is empty, repeats the previous event, or the
    /// printer is recording too many events. Events come from <see cref="Observe"/>; this is for tests.
    /// </summary>
    internal PrinterEvent? Record(Guid printerId, PrinterSeverity severity, string code, string message)
    {
        var recorded = new List<PrinterEvent>();
        PrinterEvent? result;
        Guid historyId;
        lock (_gate)
        {
            var journal = GetJournal(printerId);
            historyId = journal.HistoryId;
            result = RecordLocked(journal, recorded, severity, code, message);
        }

        Raise(printerId, historyId, recorded);
        return result;
    }

    /// <summary>Forgets the history of every printer that is not in <paramref name="printerIds"/>.</summary>
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

    // Extensions are not trusted, so a null that the types forbid is still possible.
    private static bool IsUsable(PrinterCondition? condition) =>
        condition is not null && !string.IsNullOrWhiteSpace(condition.Code) && !string.IsNullOrWhiteSpace(condition.Message);

    private static bool IsTidy(string text, int maxLength) =>
        text.Length <= maxLength && !char.IsWhiteSpace(text[0]) && !char.IsWhiteSpace(text[^1]);

    // The cheap check Sanitize starts with: no allocation when a snapshot already fits.
    private bool IsClean(IReadOnlyList<PrinterCondition>? conditions)
    {
        if (conditions is null || conditions.Count > Options.MaxConditions)
        {
            return false;
        }

        for (var i = 0; i < conditions.Count; i++)
        {
            var condition = conditions[i];
            if (!IsUsable(condition)
                || !IsTidy(condition.Code, Options.MaxCodeLength)
                || !IsTidy(condition.Message, Options.MaxMessageLength))
            {
                return false;
            }

            for (var earlier = 0; earlier < i; earlier++)
            {
                if (conditions[earlier].Code == condition.Code)
                {
                    return false;
                }
            }
        }

        return true;
    }

    // Whether the snapshot's conditions are what the journal already knows: exactly, or (while offline) at most.
    private static bool Covers(Dictionary<string, PrinterCondition> known, IReadOnlyList<PrinterCondition> conditions, bool exact)
    {
        if (exact && known.Count != conditions.Count)
        {
            return false;
        }

        foreach (var condition in conditions)
        {
            if (!known.TryGetValue(condition.Code, out var existing) || existing != condition)
            {
                return false;
            }
        }

        return true;
    }

    private static string Limit(string text, int maxLength) =>
        text.Length <= maxLength ? text : string.Concat(text.AsSpan(0, Math.Max(0, maxLength - Ellipsis.Length)), Ellipsis);

    private Journal GetJournal(Guid printerId)
    {
        if (!_journals.TryGetValue(printerId, out var journal))
        {
            journal = new Journal(Options.Burst, _timeProvider.GetUtcNow());
            _journals[printerId] = journal;
        }

        return journal;
    }

    private PrinterEvent? RecordLocked(
        Journal journal, List<PrinterEvent> recorded, PrinterSeverity severity, string code, string message, bool rateLimited = true)
    {
        code = Limit(code.Trim(), Options.MaxCodeLength);
        message = Limit(message.Trim(), Options.MaxMessageLength);
        if (code.Length == 0 || message.Length == 0)
        {
            return null;
        }

        // A connector that reports the same thing over and over must not push real history out.
        if (journal.Events.Count > 0)
        {
            var last = journal.Newest;
            if (last.Severity == severity && last.Code == code && last.Message == message)
            {
                return null;
            }
        }

        var now = _timeProvider.GetUtcNow();

        // Only information is rate-limited. A warning or an error is never dropped because a noisy connector used up
        // the burst; repeats of it are already stopped above, and the capacity bounds what is kept.
        if (rateLimited && severity == PrinterSeverity.Info)
        {
            journal.Tokens = Math.Min(
                Options.Burst,
                journal.Tokens + Math.Max(0, (now - journal.RefilledAt) / Options.EffectiveRefillInterval));
            journal.RefilledAt = now;

            if (journal.Tokens < 1)
            {
                journal.Suppressed++;
                return null;
            }

            journal.Tokens--;
        }

        if (journal.Suppressed > 0)
        {
            Append(journal, recorded, now, PrinterSeverity.Warning, "events.suppressed",
                $"{journal.Suppressed} events were skipped because the printer reported too many.");
            journal.Suppressed = 0;
        }

        return Append(journal, recorded, now, severity, code, message);
    }

    private PrinterEvent Append(
        Journal journal, List<PrinterEvent> recorded, DateTimeOffset now, PrinterSeverity severity, string code, string message)
    {
        var appended = new PrinterEvent(++journal.LastSequence, now, severity, code, message);
        journal.Events.Enqueue(appended);
        journal.Newest = appended;
        recorded.Add(appended);

        while (journal.Events.Count > Options.Capacity)
        {
            journal.Events.Dequeue();
        }

        return appended;
    }

    private void Raise(Guid printerId, Guid historyId, List<PrinterEvent> recorded)
    {
        if (recorded.Count == 0 || Recorded is not { } handlers)
        {
            return;
        }

        foreach (var recordedEvent in recorded)
        {
            var notice = new PrinterEventRecorded(printerId, historyId, recordedEvent);
            foreach (var handler in handlers.GetInvocationList().Cast<Action<PrinterEventRecorded>>())
            {
                try
                {
                    handler(notice);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "A handler of printer event {Sequence} for printer {PrinterId} failed",
                        recordedEvent.Sequence, printerId);
                }
            }
        }
    }

    private static PrinterSeverity StateSeverity(UniversalPrinterState state) => state switch
    {
        UniversalPrinterState.Error => PrinterSeverity.Error,
        UniversalPrinterState.Failed or UniversalPrinterState.Offline => PrinterSeverity.Warning,
        _ => PrinterSeverity.Info
    };

    private sealed class Journal
    {
        public Journal(double tokens, DateTimeOffset now)
        {
            Tokens = tokens;
            RefilledAt = now;
        }

        public Dictionary<string, PrinterCondition> Conditions { get; set; } = [];

        public Queue<PrinterEvent> Events { get; } = new();

        /// <summary>Changes whenever the printer's history starts over, so clients can tell two histories apart.</summary>
        public Guid HistoryId { get; } = Guid.NewGuid();

        public long LastSequence { get; set; }

        public PrinterEvent Newest { get; set; } = null!;

        public DateTimeOffset RefilledAt { get; set; }

        public UniversalPrinterState State { get; set; } = UniversalPrinterState.Offline;

        public int Suppressed { get; set; }

        public double Tokens { get; set; }
    }
}
