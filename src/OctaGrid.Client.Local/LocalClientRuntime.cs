using OctaGrid.Agent.Core;
using OctaGrid.Client.Contracts;

namespace OctaGrid.Client.Local;

internal sealed class LocalClientRuntime(AgentWorker agentWorker, LocalAgentSynchronizer synchronizer) : IClientRuntime
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Runs for the app's lifetime; ends when cancellationToken is cancelled.
        _ = synchronizer.RunAsync(cancellationToken);
        return agentWorker.ExecuteAsync(cancellationToken);
    }
}
