using OctaGrid.Application.Agents;
using OctaGrid.Client.Contracts;
using OctaGrid.Protocol.AgentLink.Requests;
using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Client.Local;

/// <summary>
/// Extension management, sent as messages over the <see cref="IAgentLink"/> to the agent each call names. In Local
/// mode the link reaches only the in-process agent and rejects any other id.
/// </summary>
internal sealed class LocalExtensionsClient : IExtensionsClient
{
    private readonly IAgentLink _link;

    public LocalExtensionsClient(IAgentLink link)
    {
        _link = link;
    }

    public async Task<IReadOnlyList<ExtensionInfo>> GetExtensionsAsync(Guid agentId, CancellationToken cancellationToken) =>
        (await _link.SendAsync(agentId, new GetExtensionsRequest(), cancellationToken)).Extensions;

    public async Task SetExtensionEnabledAsync(Guid agentId, string extensionId, bool enabled, CancellationToken cancellationToken) =>
        await _link.SendAsync(agentId, new SetExtensionEnabledRequest(extensionId, enabled), cancellationToken);

    public async Task<ExtensionInstallResult> InstallExtensionAsync(Guid agentId, Stream package, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);

        var uploadId = await _link.UploadAsync(agentId, package, cancellationToken);
        try
        {
            return (await _link.SendAsync(agentId, new InstallExtensionRequest(uploadId), cancellationToken)).Result;
        }
        catch (OperationCanceledException)
        {
            // Cancelled before the agent took the upload: do not leave it behind.
            await AgentLinkUploads.AbortQuietlyAsync(_link, agentId, uploadId);
            throw;
        }
    }

    public async Task<ExtensionInstallResult> ApproveExtensionAsync(Guid agentId, string extensionId, CancellationToken cancellationToken) =>
        (await _link.SendAsync(agentId, new ApproveExtensionRequest(extensionId), cancellationToken)).Result;

    public async Task<ExtensionInstallResult> UninstallExtensionAsync(Guid agentId, string extensionId, CancellationToken cancellationToken) =>
        (await _link.SendAsync(agentId, new UninstallExtensionRequest(extensionId), cancellationToken)).Result;

    public async Task<string?> GetDeveloperFolderAsync(Guid agentId, CancellationToken cancellationToken) =>
        (await _link.SendAsync(agentId, new GetDeveloperFolderRequest(), cancellationToken)).Folder;

    public async Task<ExtensionInstallResult> SetDeveloperFolderAsync(Guid agentId, string? folder, CancellationToken cancellationToken) =>
        (await _link.SendAsync(agentId, new SetDeveloperFolderRequest(folder), cancellationToken)).Result;

    public async Task<ExtensionInstallResult> ReloadDeveloperExtensionsAsync(Guid agentId, CancellationToken cancellationToken) =>
        (await _link.SendAsync(agentId, new ReloadDeveloperExtensionsRequest(), cancellationToken)).Result;
}
