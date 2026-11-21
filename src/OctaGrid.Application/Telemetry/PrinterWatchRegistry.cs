namespace OctaGrid.Application.Telemetry;

/// <summary>
/// Which printers each client connection (watcher) shows, and through that which printers each agent has to stream.
/// One per backend process, shared by every <see cref="Services.PrinterWatchService"/>. Not thread-safe on its own:
/// callers hold <see cref="Gate"/> while they change it and send the result, so agents get the sets in the order they
/// were made.
/// </summary>
public sealed class PrinterWatchRegistry
{
    private readonly HashSet<Guid> _stale = [];
    private readonly Dictionary<Guid, IReadOnlyDictionary<Guid, Guid>> _watchers = [];

    /// <summary>Held while the sets change and the agents are told.</summary>
    public SemaphoreSlim Gate { get; } = new(1, 1);

    /// <summary>
    /// Records that an agent may not have its current set, because sending it failed. The next <see cref="Replace"/>
    /// returns that agent again, whether or not its set changed.
    /// </summary>
    public void MarkStale(Guid agentId) => _stale.Add(agentId);

    /// <summary>
    /// Replaces what a watcher shows.
    /// </summary>
    /// <param name="watcherId">The client connection.</param>
    /// <param name="printerAgents">Each printer the watcher shows, with the agent it is assigned to.</param>
    /// <returns>
    /// The printers the watcher showed before, and the agents that have to be sent their set: those whose set changed
    /// and those marked stale, each with its complete set (empty when nobody watches any of its printers any more).
    /// </returns>
    public (IReadOnlySet<Guid> Previous, IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> ChangedAgents) Replace(
        Guid watcherId, IReadOnlyDictionary<Guid, Guid> printerAgents)
    {
        var before = WatchedByAgent();
        var previous = _watchers.TryGetValue(watcherId, out var old) ? old.Keys.ToHashSet() : [];

        if (printerAgents.Count == 0)
        {
            _watchers.Remove(watcherId);
        }
        else
        {
            _watchers[watcherId] = printerAgents;
        }

        var after = WatchedByAgent();
        var changed = new Dictionary<Guid, IReadOnlyList<Guid>>();
        foreach (var agentId in before.Keys.Union(after.Keys).Union(_stale))
        {
            var was = before.GetValueOrDefault(agentId) ?? [];
            var now = after.GetValueOrDefault(agentId) ?? [];
            if (!was.SetEquals(now) || _stale.Contains(agentId))
            {
                changed[agentId] = now.ToList();
            }
        }

        // Returned to be sent; the caller marks them again if sending fails.
        _stale.Clear();

        return (previous, changed);
    }

    private Dictionary<Guid, HashSet<Guid>> WatchedByAgent()
    {
        var result = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var (printerId, agentId) in _watchers.Values.SelectMany(w => w))
        {
            if (!result.TryGetValue(agentId, out var printers))
            {
                printers = [];
                result[agentId] = printers;
            }

            printers.Add(printerId);
        }

        return result;
    }
}
