using Microsoft.Extensions.Logging;
using OctaGrid.Application.Services;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Local;

/// <summary>
/// Keeps the local agent's set of printers equal to the saved printers assigned to it: it sends the complete set when the agent
/// starts and again whenever a printer changes. Remote mode's equivalent is the server doing the same over
/// SignalR when an agent connects or a printer assigned to it changes.
/// </summary>
internal sealed class LocalAgentSynchronizer
{
    private readonly LocalCalls _calls;
    private readonly EntityChangeFeed _feed;
    private readonly LocalAgent _localAgent;
    private readonly ILogger<LocalAgentSynchronizer> _logger;

    public LocalAgentSynchronizer(EntityChangeFeed feed, LocalCalls calls, LocalAgent localAgent, ILogger<LocalAgentSynchronizer> logger)
    {
        _feed = feed;
        _calls = calls;
        _localAgent = localAgent;
        _logger = logger;
    }

    /// <summary>Sends the local agent the printers assigned to it.</summary>
    public Task PushProfilesAsync(CancellationToken cancellationToken)
    {
        var agentId = _localAgent.Id;
        return _calls.RunAsBackend<AgentProfilesService>(profiles => profiles.Push(agentId, cancellationToken));
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
