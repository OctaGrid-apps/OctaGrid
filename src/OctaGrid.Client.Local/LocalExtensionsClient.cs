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

    public Task<ExtensionInstallResult> InstallExtensionAsync(string packagePath, CancellationToken cancellationToken) =>
        Task.Run(() => agentWorker.InstallExtension(packagePath), cancellationToken);

    public Task<ExtensionInstallResult> ApproveExtensionAsync(string extensionId, CancellationToken cancellationToken) =>
        agentWorker.ApproveExtensionAsync(extensionId, cancellationToken);

    public Task<ExtensionInstallResult> UninstallExtensionAsync(string extensionId, CancellationToken cancellationToken) =>
        agentWorker.UninstallExtensionAsync(extensionId, cancellationToken);

    public Task<string?> GetDeveloperFolderAsync(CancellationToken cancellationToken) =>
        Task.FromResult(agentWorker.DeveloperFolder);

    public Task<ExtensionInstallResult> SetDeveloperFolderAsync(string? folder, CancellationToken cancellationToken) =>
        agentWorker.SetDeveloperFolderAsync(folder, cancellationToken);

    public Task<ExtensionInstallResult> ReloadDeveloperExtensionsAsync(CancellationToken cancellationToken) =>
        agentWorker.ReloadDeveloperExtensionsAsync(cancellationToken);
}
