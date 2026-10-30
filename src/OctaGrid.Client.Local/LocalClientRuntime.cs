using OctaGrid.Agent.Core;
using OctaGrid.Client.Contracts;

namespace OctaGrid.Client.Local;

internal sealed class LocalClientRuntime(AgentWorker agentWorker, LocalAgentSynchronizer synchronizer) : IClientRuntime
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await agentWorker.ExecuteAsync(cancellationToken);
            // Runs for the app's lifetime; ends when cancellationToken is cancelled.
            await synchronizer.RunAsync(cancellationToken);
        }
        finally
        {
            await agentWorker.DisposeAsync();
        }
    }
}
