using OctaGrid.Application.Agents;
using OctaGrid.Application.Authorization;
using OctaGrid.Protocol.AgentLink.Requests;
using OctaGrid.Protocol.Extensions;
using OctaGrid.Protocol.Permissions;

namespace OctaGrid.Application.Services;

/// <summary>
/// Extension management on one agent. Every call checks that the user is a member of the agent's workspace
/// before anything is sent over the <see cref="IAgentLink"/>.
/// </summary>
public sealed class ExtensionsService
{
    private readonly WorkspaceAccess _access;
    private readonly IAgentLink _link;

    public ExtensionsService(WorkspaceAccess access, IAgentLink link)
    {
        _access = access;
        _link = link;
    }

    public async Task<ExtensionInstallResult> Approve(Guid userId, Guid agentId, string extensionId, CancellationToken cancellationToken)
    {
        await _access.RequireForAgent(userId, agentId, Permission.ManageExtensions, cancellationToken);
        return (await _link.SendAsync(agentId, new ApproveExtensionRequest(extensionId), cancellationToken)).Result;
    }

    public async Task<string?> GetDeveloperFolder(Guid userId, Guid agentId, CancellationToken cancellationToken)
    {
        await _access.RequireForAgent(userId, agentId, Permission.View, cancellationToken);
        return (await _link.SendAsync(agentId, new GetDeveloperFolderRequest(), cancellationToken)).Folder;
    }

    public async Task<IReadOnlyList<ExtensionInfo>> GetList(Guid userId, Guid agentId, CancellationToken cancellationToken)
    {
        await _access.RequireForAgent(userId, agentId, Permission.View, cancellationToken);
        return (await _link.SendAsync(agentId, new GetExtensionsRequest(), cancellationToken)).Extensions;
    }

    public async Task<ExtensionInstallResult> Install(Guid userId, Guid agentId, Stream package, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        await _access.RequireForAgent(userId, agentId, Permission.ManageExtensions, cancellationToken);

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

    public async Task<ExtensionInstallResult> ReloadDeveloperExtensions(Guid userId, Guid agentId, CancellationToken cancellationToken)
    {
        await _access.RequireForAgent(userId, agentId, Permission.ManageExtensions, cancellationToken);
        return (await _link.SendAsync(agentId, new ReloadDeveloperExtensionsRequest(), cancellationToken)).Result;
    }

    public async Task<ExtensionInstallResult> SetDeveloperFolder(Guid userId, Guid agentId, string? folder, CancellationToken cancellationToken)
    {
        await _access.RequireForAgent(userId, agentId, Permission.ManageExtensions, cancellationToken);
        return (await _link.SendAsync(agentId, new SetDeveloperFolderRequest(folder), cancellationToken)).Result;
    }

    public async Task SetEnabled(Guid userId, Guid agentId, string extensionId, bool enabled, CancellationToken cancellationToken)
    {
        await _access.RequireForAgent(userId, agentId, Permission.ManageExtensions, cancellationToken);
        await _link.SendAsync(agentId, new SetExtensionEnabledRequest(extensionId, enabled), cancellationToken);
    }

    public async Task<ExtensionInstallResult> Uninstall(Guid userId, Guid agentId, string extensionId, CancellationToken cancellationToken)
    {
        await _access.RequireForAgent(userId, agentId, Permission.ManageExtensions, cancellationToken);
        return (await _link.SendAsync(agentId, new UninstallExtensionRequest(extensionId), cancellationToken)).Result;
    }
}
