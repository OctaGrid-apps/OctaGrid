namespace OctaGrid.Application.Telemetry;

/// <summary>
/// Which printers each client connection (watcher) shows, and through that which printers each agent has to stream.
/// One per backend process, shared by every <see cref="Services.PrinterWatchService"/>. Not thread-safe on its own:
/// callers hold <see cref="Gate"/> while they change it and send the result, so agents get the sets in the order they
/// were made.
/// </summary>
public sealed class PrinterWatchRegistry
{
    private readonly Dictionary<Guid, IReadOnlyDictionary<Guid, Guid>> _watchers = [];

    /// <summary>Held while the sets change and the agents are told.</summary>
    public SemaphoreSlim Gate { get; } = new(1, 1);

    /// <summary>
    /// Replaces what a watcher shows.
    /// </summary>
    /// <param name="watcherId">The client connection.</param>
    /// <param name="printerAgents">Each printer the watcher shows, with the agent it is assigned to.</param>
    /// <returns>
    /// The printers the watcher showed before, and the agents whose set of watched printers changed, each with its new
    /// complete set (empty when nobody watches any of its printers any more).
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
        foreach (var agentId in before.Keys.Union(after.Keys))
        {
            var was = before.GetValueOrDefault(agentId) ?? [];
            var now = after.GetValueOrDefault(agentId) ?? [];
            if (!was.SetEquals(now))
            {
                changed[agentId] = now.ToList();
            }
        }

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
