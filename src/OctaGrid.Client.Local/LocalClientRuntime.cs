using OctaGrid.Agent.Core;
using OctaGrid.Client.Contracts;

namespace OctaGrid.Client.Local;

internal sealed class LocalClientRuntime(
    AgentWorker agentWorker, LocalAgentSynchronizer synchronizer, IClientMessageHandler messageHandler) : IClientRuntime
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Registered before the agent starts, so no event recorded while printers connect is missed.
        agentWorker.EventRecorded += messageHandler.Handle;
        try
        {
            await agentWorker.ExecuteAsync(cancellationToken);
            // Runs for the app's lifetime; ends when cancellationToken is cancelled.
            await synchronizer.RunAsync(cancellationToken);
        }
        finally
        {
            agentWorker.EventRecorded -= messageHandler.Handle;
            await agentWorker.DisposeAsync();
        }
    }
}
