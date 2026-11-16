using OctaGrid.Application.Services;
using OctaGrid.Client.Contracts;
using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Client.Local;

/// <summary>
/// Extension management, handed to <see cref="ExtensionsService"/> as the local user. The service checks that the
/// user may manage the agent each call names; in Local mode the link then reaches only the in-process agent.
/// </summary>
internal sealed class LocalExtensionsClient : IExtensionsClient
{
    private readonly LocalCalls _calls;

    public LocalExtensionsClient(LocalCalls calls)
    {
        _calls = calls;
    }

    public Task<IReadOnlyList<ExtensionInfo>> GetExtensionsAsync(Guid agentId, CancellationToken cancellationToken) =>
        _calls.Run<ExtensionsService, IReadOnlyList<ExtensionInfo>>((extensions, userId) => extensions.GetList(userId, agentId, cancellationToken), cancellationToken);

    public Task SetExtensionEnabledAsync(Guid agentId, string extensionId, bool enabled, CancellationToken cancellationToken) =>
        _calls.Run<ExtensionsService>((extensions, userId) => extensions.SetEnabled(userId, agentId, extensionId, enabled, cancellationToken), cancellationToken);

    public Task<ExtensionInstallResult> InstallExtensionAsync(Guid agentId, Stream package, CancellationToken cancellationToken) =>
        _calls.Run<ExtensionsService, ExtensionInstallResult>((extensions, userId) => extensions.Install(userId, agentId, package, cancellationToken), cancellationToken);

    public Task<ExtensionInstallResult> ApproveExtensionAsync(Guid agentId, string extensionId, CancellationToken cancellationToken) =>
        _calls.Run<ExtensionsService, ExtensionInstallResult>((extensions, userId) => extensions.Approve(userId, agentId, extensionId, cancellationToken), cancellationToken);

    public Task<ExtensionInstallResult> UninstallExtensionAsync(Guid agentId, string extensionId, CancellationToken cancellationToken) =>
        _calls.Run<ExtensionsService, ExtensionInstallResult>((extensions, userId) => extensions.Uninstall(userId, agentId, extensionId, cancellationToken), cancellationToken);

    public Task<string?> GetDeveloperFolderAsync(Guid agentId, CancellationToken cancellationToken) =>
        _calls.Run<ExtensionsService, string?>((extensions, userId) => extensions.GetDeveloperFolder(userId, agentId, cancellationToken), cancellationToken);

    public Task<ExtensionInstallResult> SetDeveloperFolderAsync(Guid agentId, string? folder, CancellationToken cancellationToken) =>
        _calls.Run<ExtensionsService, ExtensionInstallResult>((extensions, userId) => extensions.SetDeveloperFolder(userId, agentId, folder, cancellationToken), cancellationToken);

    public Task<ExtensionInstallResult> ReloadDeveloperExtensionsAsync(Guid agentId, CancellationToken cancellationToken) =>
        _calls.Run<ExtensionsService, ExtensionInstallResult>((extensions, userId) => extensions.ReloadDeveloperExtensions(userId, agentId, cancellationToken), cancellationToken);
}
