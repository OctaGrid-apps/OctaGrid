using System.IO.Compression;
using OctaGrid.Application.Agents;
using OctaGrid.Client.Contracts;
using OctaGrid.Protocol.AgentLink.Requests;
using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Client.Local;

/// <summary>Extension management for the local agent, sent as messages over the <see cref="IAgentLink"/>.</summary>
internal sealed class LocalExtensionsClient : IExtensionsClient
{
    private readonly IAgentLink _link;

    public LocalExtensionsClient(IAgentLink link)
    {
        _link = link;
    }

    public async Task<IReadOnlyList<ExtensionInfo>> GetExtensionsAsync(CancellationToken cancellationToken) =>
        (await _link.SendAsync(LocalAgent.Id, new GetExtensionsRequest(), cancellationToken)).Extensions;

    public async Task SetExtensionEnabledAsync(string extensionId, bool enabled, CancellationToken cancellationToken) =>
        await _link.SendAsync(LocalAgent.Id, new SetExtensionEnabledRequest(extensionId, enabled), cancellationToken);

    /// <summary>
    /// Sends the package to the agent and asks it to install it. An agent takes a package as one file, so a
    /// folder is zipped first.
    /// </summary>
    public async Task<ExtensionInstallResult> InstallExtensionAsync(string packagePath, CancellationToken cancellationToken)
    {
        if (Directory.Exists(packagePath))
        {
            var zipped = Path.Combine(Path.GetTempPath(), $"octagrid-{Guid.NewGuid():N}.zip");
            try
            {
                await Task.Run(() => ZipFile.CreateFromDirectory(packagePath, zipped), cancellationToken);
                return await InstallFileAsync(zipped, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return ExtensionInstallResult.Fail($"The folder could not be read: {ex.Message}");
            }
            finally
            {
                File.Delete(zipped);
            }
        }

        if (!File.Exists(packagePath))
        {
            return ExtensionInstallResult.Fail("That file or folder does not exist.");
        }

        try
        {
            return await InstallFileAsync(packagePath, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ExtensionInstallResult.Fail($"The file could not be read: {ex.Message}");
        }
    }

    public async Task<ExtensionInstallResult> ApproveExtensionAsync(string extensionId, CancellationToken cancellationToken) =>
        (await _link.SendAsync(LocalAgent.Id, new ApproveExtensionRequest(extensionId), cancellationToken)).Result;

    public async Task<ExtensionInstallResult> UninstallExtensionAsync(string extensionId, CancellationToken cancellationToken) =>
        (await _link.SendAsync(LocalAgent.Id, new UninstallExtensionRequest(extensionId), cancellationToken)).Result;

    public async Task<string?> GetDeveloperFolderAsync(CancellationToken cancellationToken) =>
        (await _link.SendAsync(LocalAgent.Id, new GetDeveloperFolderRequest(), cancellationToken)).Folder;

    public async Task<ExtensionInstallResult> SetDeveloperFolderAsync(string? folder, CancellationToken cancellationToken) =>
        (await _link.SendAsync(LocalAgent.Id, new SetDeveloperFolderRequest(folder), cancellationToken)).Result;

    public async Task<ExtensionInstallResult> ReloadDeveloperExtensionsAsync(CancellationToken cancellationToken) =>
        (await _link.SendAsync(LocalAgent.Id, new ReloadDeveloperExtensionsRequest(), cancellationToken)).Result;

    private async Task<ExtensionInstallResult> InstallFileAsync(string path, CancellationToken cancellationToken)
    {
        Guid uploadId;
        await using (var content = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
        {
            uploadId = await _link.UploadAsync(LocalAgent.Id, content, cancellationToken);
        }

        return (await _link.SendAsync(LocalAgent.Id, new InstallExtensionRequest(uploadId), cancellationToken)).Result;
    }
}
