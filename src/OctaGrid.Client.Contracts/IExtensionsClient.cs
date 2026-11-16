using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Client.Contracts;

/// <summary>
/// Lists the extensions installed on an agent and lets the user turn them on or off, install, approve and remove them.
/// Extensions belong to an agent, not to the client: every call names the agent it is about.
/// </summary>
public interface IExtensionsClient
{
    /// <summary>Trusts an installed extension and turns it on.</summary>
    Task<ExtensionInstallResult> ApproveExtensionAsync(Guid agentId, string extensionId, CancellationToken cancellationToken);

    /// <summary>
    /// The folder, on the agent's machine, extensions are loaded from in developer mode, or null when developer mode is off.
    /// </summary>
    Task<string?> GetDeveloperFolderAsync(Guid agentId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ExtensionInfo>> GetExtensionsAsync(Guid agentId, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a package (a <c>.octaext</c> or <c>.zip</c> file) to the agent and installs it there. Nothing from it runs
    /// until it is approved. The client reads the package, so it works where the client has no file paths (WASM).
    /// </summary>
    /// <param name="package">The package content, read from its current position to the end. The caller disposes it.</param>
    Task<ExtensionInstallResult> InstallExtensionAsync(Guid agentId, Stream package, CancellationToken cancellationToken);

    /// <summary>Loads the developer folder again, for example right after a build.</summary>
    Task<ExtensionInstallResult> ReloadDeveloperExtensionsAsync(Guid agentId, CancellationToken cancellationToken);

    /// <summary>
    /// Turns developer mode on for a folder on the agent's machine (or off with null). Extensions in it load without
    /// asking and reload when it changes.
    /// </summary>
    Task<ExtensionInstallResult> SetDeveloperFolderAsync(Guid agentId, string? folder, CancellationToken cancellationToken);

    Task SetExtensionEnabledAsync(Guid agentId, string extensionId, bool enabled, CancellationToken cancellationToken);

    /// <summary>Removes an installed extension.</summary>
    Task<ExtensionInstallResult> UninstallExtensionAsync(Guid agentId, string extensionId, CancellationToken cancellationToken);
}
