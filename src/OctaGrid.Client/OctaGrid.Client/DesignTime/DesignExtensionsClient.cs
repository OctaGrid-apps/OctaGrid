using OctaGrid.Client.Contracts;
using OctaGrid.Extensions.Models;
using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Client.DesignTime;

/// <summary>Sample extensions for the designer and for tests.</summary>
internal sealed class DesignExtensionsClient : IExtensionsClient
{
    private const ConnectorCapabilities All =
        ConnectorCapabilities.PrintControl | ConnectorCapabilities.Files | ConnectorCapabilities.Thumbnails;

    private readonly List<ExtensionInfo> _extensions =
    [
        new("octagrid.bambu-mqtt", "Bambu Lab (LAN mode)", "1.0.0", "OctaGrid", "connector",
            ExtensionSource.BuiltIn, ExtensionState.Loaded, null, All, CanUninstall: false),
        new("octagrid.moonraker", "Moonraker (Klipper)", "1.0.0", "OctaGrid", "connector",
            ExtensionSource.BuiltIn, ExtensionState.Loaded, null, All, CanUninstall: false),
        new("acme.prusalink", "PrusaLink", "1.2.0", "Acme", "connector",
            ExtensionSource.UserFolder, ExtensionState.NeedsApproval, "Review and approve this extension to run it. Its code has not been loaded.",
            ConnectorCapabilities.None, CanUninstall: true, Permissions: ["network"], ProjectUrl: "https://github.com/acme/octagrid-prusalink"),
    ];

    public Task<IReadOnlyList<ExtensionInfo>> GetExtensionsAsync(Guid agentId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ExtensionInfo>>(_extensions.ToList());

    public Task SetExtensionEnabledAsync(Guid agentId, string extensionId, bool enabled, CancellationToken cancellationToken)
    {
        Replace(extensionId, e => e with { State = enabled ? ExtensionState.Loaded : ExtensionState.Disabled });
        return Task.CompletedTask;
    }

    /// <summary>The bytes of the last package sent to <see cref="InstallExtensionAsync"/>, for tests.</summary>
    public byte[]? LastPackage { get; private set; }

    public async Task<ExtensionInstallResult> InstallExtensionAsync(Guid agentId, Stream package, CancellationToken cancellationToken)
    {
        using var copy = new MemoryStream();
        await package.CopyToAsync(copy, cancellationToken);
        LastPackage = copy.ToArray();
        return ExtensionInstallResult.Fail("Installing is not available in the designer.");
    }

    public Task<ExtensionInstallResult> ApproveExtensionAsync(Guid agentId, string extensionId, CancellationToken cancellationToken)
    {
        Replace(extensionId, e => e with { State = ExtensionState.Loaded, StateMessage = null, Capabilities = All });
        return Task.FromResult(ExtensionInstallResult.Ok(extensionId, "Turned on."));
    }

    public Task<ExtensionInstallResult> UninstallExtensionAsync(Guid agentId, string extensionId, CancellationToken cancellationToken)
    {
        _extensions.RemoveAll(e => e.Id == extensionId && e.CanUninstall);
        return Task.FromResult(ExtensionInstallResult.Ok(extensionId, "Removed."));
    }

    private string? _developerFolder;

    public Task<string?> GetDeveloperFolderAsync(Guid agentId, CancellationToken cancellationToken) => Task.FromResult(_developerFolder);

    public Task<ExtensionInstallResult> SetDeveloperFolderAsync(Guid agentId, string? folder, CancellationToken cancellationToken)
    {
        _developerFolder = string.IsNullOrWhiteSpace(folder) ? null : folder;
        return Task.FromResult(ExtensionInstallResult.Ok(null, _developerFolder is null ? "Developer mode is off." : "Developer mode is on."));
    }

    public Task<ExtensionInstallResult> ReloadDeveloperExtensionsAsync(Guid agentId, CancellationToken cancellationToken) =>
        Task.FromResult(ExtensionInstallResult.Ok(null, "Developer extensions reloaded."));

    private void Replace(string extensionId, Func<ExtensionInfo, ExtensionInfo> change)
    {
        var index = _extensions.FindIndex(e => e.Id == extensionId);
        if (index >= 0)
        {
            _extensions[index] = change(_extensions[index]);
        }
    }
}
