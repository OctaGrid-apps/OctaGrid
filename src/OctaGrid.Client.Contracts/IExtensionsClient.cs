using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Client.Contracts;

/// <summary>Lists the installed extensions and lets the user turn them on or off, install, approve and remove them.</summary>
public interface IExtensionsClient
{
    Task<IReadOnlyList<ExtensionInfo>> GetExtensionsAsync(CancellationToken cancellationToken);

    Task SetExtensionEnabledAsync(string extensionId, bool enabled, CancellationToken cancellationToken);

    /// <summary>Installs a package (a folder or a <c>.octaext</c> file). Nothing from it runs until it is approved.</summary>
    Task<ExtensionInstallResult> InstallExtensionAsync(string packagePath, CancellationToken cancellationToken);

    /// <summary>Trusts an installed extension and turns it on.</summary>
    Task<ExtensionInstallResult> ApproveExtensionAsync(string extensionId, CancellationToken cancellationToken);

    /// <summary>Removes an installed extension.</summary>
    Task<ExtensionInstallResult> UninstallExtensionAsync(string extensionId, CancellationToken cancellationToken);

    /// <summary>The folder extensions are loaded from in developer mode, or null when developer mode is off.</summary>
    Task<string?> GetDeveloperFolderAsync(CancellationToken cancellationToken);

    /// <summary>Turns developer mode on for a folder (or off with null). Extensions in it load without asking and reload when it changes.</summary>
    Task<ExtensionInstallResult> SetDeveloperFolderAsync(string? folder, CancellationToken cancellationToken);

    /// <summary>Loads the developer folder again, for example right after a build.</summary>
    Task<ExtensionInstallResult> ReloadDeveloperExtensionsAsync(CancellationToken cancellationToken);
}
