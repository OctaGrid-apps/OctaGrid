namespace OctaGrid.Protocol.Extensions;

/// <summary>Where an extension came from.</summary>
public enum ExtensionSource
{
    /// <summary>Ships inside OctaGrid. Can be disabled but not removed.</summary>
    Bundled,

    /// <summary>Installed by the user into the extensions folder.</summary>
    UserFolder,

    /// <summary>Installed as a separate Microsoft Store package.</summary>
    Store,
}

/// <summary>What the host did with an extension.</summary>
public enum ExtensionState
{
    /// <summary>Loaded and available.</summary>
    Loaded,

    /// <summary>Turned off by the user.</summary>
    Disabled,

    /// <summary>Not loaded because it does not work with this version of OctaGrid.</summary>
    Incompatible,

    /// <summary>Could not be loaded, or stopped working.</summary>
    Failed,

    /// <summary>Installed by the user but not run yet: its code is loaded only after the user trusts the publisher.</summary>
    NeedsApproval,
}

/// <summary>An installed extension as shown in the extension management UI.</summary>
/// <param name="Id">Extension id (<c>publisher.name</c>).</param>
/// <param name="Name">Name shown to users.</param>
/// <param name="Version">Version of the extension.</param>
/// <param name="Publisher">Publisher shown to users.</param>
/// <param name="Kind">Kind of extension, for example <c>connector</c>.</param>
/// <param name="Source">Where it was installed from.</param>
/// <param name="State">Whether it is active.</param>
/// <param name="StateMessage">Explains <see cref="ExtensionState.Incompatible"/> and <see cref="ExtensionState.Failed"/>.</param>
/// <param name="Capabilities">What the extension can do, when it is a connector.</param>
/// <param name="CanUninstall">Whether the user can remove it.</param>
/// <param name="ActivePrinters">How many printers are connected through this extension right now.</param>
/// <param name="Permissions">What the extension says it needs (for example <c>network</c>), shown when the user is asked to trust it.</param>
public sealed record ExtensionInfo(
    string Id,
    string Name,
    string Version,
    string Publisher,
    string Kind,
    ExtensionSource Source,
    ExtensionState State,
    string? StateMessage,
    ConnectorCapabilities Capabilities,
    bool CanUninstall,
    int ActivePrinters = 0,
    IReadOnlyList<string>? Permissions = null);
