using OctaGrid.Client.Contracts;
using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Client.Services;

/// <summary>Sample data for the designer and for modes that do not have an agent yet.</summary>
internal sealed class DesignExtensionsClient : IExtensionsClient
{
    private const ConnectorCapabilities All =
        ConnectorCapabilities.PrintControl | ConnectorCapabilities.Files | ConnectorCapabilities.Thumbnails;

    private readonly List<ExtensionInfo> _extensions =
    [
        new("octagrid.bambu-mqtt", "Bambu Lab (LAN mode)", "1.0.0", "OctaGrid", "connector",
            ExtensionSource.Bundled, ExtensionState.Loaded, null, All, CanUninstall: false),
        new("octagrid.moonraker", "Moonraker (Klipper)", "1.0.0", "OctaGrid", "connector",
            ExtensionSource.Bundled, ExtensionState.Loaded, null, All, CanUninstall: false),
    ];

    public Task<IReadOnlyList<ExtensionInfo>> GetExtensionsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ExtensionInfo>>(_extensions.ToList());

    public Task SetExtensionEnabledAsync(string extensionId, bool enabled, CancellationToken cancellationToken)
    {
        var index = _extensions.FindIndex(e => e.Id == extensionId);
        if (index >= 0)
        {
            _extensions[index] = _extensions[index] with
            {
                State = enabled ? ExtensionState.Loaded : ExtensionState.Disabled,
            };
        }

        return Task.CompletedTask;
    }
}
