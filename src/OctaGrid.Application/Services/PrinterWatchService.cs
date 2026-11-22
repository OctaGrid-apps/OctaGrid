using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Application.Agents;
using OctaGrid.Application.Telemetry;
using OctaGrid.Extensions.Models;
using OctaGrid.Extensions.Models.Exceptions;
using OctaGrid.Protocol.AgentLink.Requests;
using OctaGrid.Protocol.Messaging;
using OctaGrid.Protocol.Permissions;

namespace OctaGrid.Application.Services;

/// <summary>
/// Which printers a client shows. A client sets the whole list whenever it changes; it gets the state and recent
/// history of the printers it starts showing, and the agents stream live telemetry only for printers some client shows.
/// </summary>
public sealed class PrinterWatchService
{
    private readonly IAgentLink _link;
    private readonly ILogger<PrinterWatchService> _logger;
    private readonly PrinterRouting _routing;
    private readonly PrinterWatchRegistry _registry;

    public PrinterWatchService(
        PrinterRouting routing, IAgentLink link, PrinterWatchRegistry registry, ILogger<PrinterWatchService>? logger = null)
    {
        _routing = routing;
        _link = link;
        _registry = registry;
        _logger = logger ?? NullLogger<PrinterWatchService>.Instance;
    }

    /// <summary>
    /// Replaces the printers a client connection shows, and returns the state and history of those it did not show
    /// before. A printer the user may not view, without an agent, or whose agent cannot be reached comes back offline
    /// with no history and is not streamed; such a printer does not fail the call for the others.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="watcherId">The client connection. Each connection has its own list.</param>
    /// <param name="printerIds">Every printer the connection shows; empty to show none.</param>
    /// <param name="resume">
    /// Printers the connection wants the state of again although it may already show them, each with the time of the last
    /// history sample it has: they are returned with only the history after it.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<PrinterBackfill>> Watch(
        Guid userId,
        Guid watcherId,
        IReadOnlyCollection<Guid> printerIds,
        IReadOnlyDictionary<Guid, DateTimeOffset>? resume,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(printerIds);

        var wanted = printerIds.Distinct().ToList();
        var agents = await _routing.AllowedAgentsOf(userId, wanted, Permission.View, cancellationToken);

        IReadOnlySet<Guid> previous;
        await _registry.Gate.WaitAsync(cancellationToken);
        try
        {
            (previous, var changedAgents) = _registry.Replace(watcherId, agents);

            // Streaming starts before the history is read, so nothing falls between the two; the client drops what it
            // gets twice by the sample time. An agent that did not get its set is sent it again on the next call, even
            // when nothing changed by then; one that failed with an unexpected error, and every agent after it, too.
            var unsent = changedAgents.Keys.ToList();
            try
            {
                foreach (var (agentId, watched) in changedAgents)
                {
                    if (await TrySend(agentId, () => _link.SendAsync(agentId, new SetWatchedPrintersRequest(watched), cancellationToken)))
                    {
                        unsent.Remove(agentId);
                    }
                }
            }
            finally
            {
                foreach (var agentId in unsent)
                {
                    _registry.MarkStale(agentId);
                }
            }
        }
        finally
        {
            _registry.Gate.Release();
        }

        var needed = wanted.Where(id => !previous.Contains(id) || resume?.ContainsKey(id) == true).ToList();
        var backfills = needed.ToDictionary(id => id, Offline);
        foreach (var group in needed.Where(agents.ContainsKey).GroupBy(id => agents[id]))
        {
            var queries = group.Select(id => new PrinterBackfillQuery(id, resume?.GetValueOrDefault(id))).ToList();
            await TrySend(group.Key, async () =>
            {
                var reply = await _link.SendAsync(group.Key, new GetPrinterBackfillRequest(queries), cancellationToken);
                foreach (var backfill in reply.Printers.Where(b => backfills.ContainsKey(b.PrinterId)))
                {
                    backfills[backfill.PrinterId] = backfill;
                }
            });
        }

        return needed.Select(id => backfills[id]).ToList();
    }

    private static PrinterBackfill Offline(Guid printerId) => new(printerId, new PrinterTelemetry(printerId), []);

    // An agent that is not connected must not stop the client from showing the printers of the others: its printers
    // show as offline. Sending the watched list to an agent when it connects belongs to the Remote-mode agent hub.
    // Returns whether it reached the agent.
    private async Task<bool> TrySend(Guid agentId, Func<Task> send)
    {
        try
        {
            await send();
            return true;
        }
        catch (NotFoundException)
        {
            _logger.LogInformation("Agent {AgentId} is not connected; its printers are shown as offline", agentId);
            return false;
        }
    }
}
