using Microsoft.Extensions.Logging;
using OctaGrid.Agent.Core;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Local;

/// <summary>
/// The only place that knows "a printer changed → reload the agent's device profiles".
/// Reads <see cref="EntityChangeFeed"/> and drives <see cref="AgentWorker"/>. Remote mode's
/// equivalent is the server pushing an assignment change to the agent over SignalR.
/// </summary>
internal sealed class LocalAgentSynchronizer(EntityChangeFeed feed, AgentWorker agent, ILogger<LocalAgentSynchronizer> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var change in feed.ReadAllAsync(cancellationToken))
            {
                if (change.EntityType != EntityTypes.Printer)
                {
                    continue;
                }

                try
                {
                    await agent.RefreshProfilesAsync(cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Failed to refresh agent profiles after printer {PrinterId} changed", change.EntityId);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
