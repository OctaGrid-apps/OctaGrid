using OctaGrid.Agent.Core;
using OctaGrid.Client.Contracts;
using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Client.Local;

internal sealed class LocalExtensionsClient(AgentWorker agentWorker) : IExtensionsClient
{
    public Task<IReadOnlyList<ExtensionInfo>> GetExtensionsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(agentWorker.GetExtensions());

    public Task SetExtensionEnabledAsync(string extensionId, bool enabled, CancellationToken cancellationToken) =>
        agentWorker.SetExtensionEnabledAsync(extensionId, enabled, cancellationToken);
}
