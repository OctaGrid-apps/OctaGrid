using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Client.Contracts;

/// <summary>Lists the installed extensions and lets the user turn them on or off.</summary>
public interface IExtensionsClient
{
    Task<IReadOnlyList<ExtensionInfo>> GetExtensionsAsync(CancellationToken cancellationToken);

    Task SetExtensionEnabledAsync(string extensionId, bool enabled, CancellationToken cancellationToken);
}
