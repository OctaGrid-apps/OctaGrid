using Microsoft.Extensions.Logging;
using OctaGrid.Application.Agents;
using OctaGrid.Protocol.AgentLink.Requests;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Local;

/// <summary>
/// Keeps the local agent's set of printers equal to the saved printers assigned to it: it sends the complete set when the agent
/// starts and again whenever a printer changes. Remote mode's equivalent is the server doing the same over
/// SignalR when an agent connects or a printer assigned to it changes.
/// </summary>
internal sealed class LocalAgentSynchronizer
{
    private readonly EntityChangeFeed _feed;
    private readonly IAgentLink _link;
    private readonly LocalAgent _localAgent;
    private readonly ILogger<LocalAgentSynchronizer> _logger;
    private readonly LocalPrinterProfiles _profiles;

    public LocalAgentSynchronizer(
        EntityChangeFeed feed, IAgentLink link, LocalAgent localAgent, LocalPrinterProfiles profiles, ILogger<LocalAgentSynchronizer> logger)
    {
        _feed = feed;
        _link = link;
        _localAgent = localAgent;
        _profiles = profiles;
        _logger = logger;
    }

    /// <summary>Reads the printers assigned to the local agent and sends them to it.</summary>
    public async Task PushProfilesAsync(CancellationToken cancellationToken)
    {
        var agentId = _localAgent.Id;
        var profiles = await _profiles.GetAsync(agentId, cancellationToken);
        await _link.SendAsync(agentId, new ApplyPrinterProfilesRequest(profiles), cancellationToken);
    }

    /// <summary>Sends the printers again after every printer change, until cancelled. Changes are handled one at a time, in order.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("LocalAgentSynchronizer listening for printer entity changes");
        try
        {
            await foreach (var change in _feed.ReadAllAsync(cancellationToken))
            {
                if (change.EntityType != EntityTypes.Printer)
                {
                    continue;
                }

                _logger.LogInformation("Printer {PrinterId} changed ({ChangeKind}); sending the local agent its printers", change.EntityId, change.Kind);
                try
                {
                    await PushProfilesAsync(cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Failed to send the local agent its printers after printer {PrinterId} changed", change.EntityId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("LocalAgentSynchronizer stopped");
        }
    }
}
