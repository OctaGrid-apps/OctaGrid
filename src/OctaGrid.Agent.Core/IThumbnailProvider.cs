using OctaGrid.Protocol.Printers;

namespace OctaGrid.Agent.Core;

/// <summary>
/// Optional capability for an <see cref="IDeviceClient"/> that can fetch the current print job's
/// thumbnail. Not every protocol supports this (Bambu/MQTT doesn't today), so it's a separate
/// interface rather than a member on IDeviceClient — DeviceOrchestrator dispatches to it with an
/// `is` check, the same way IDeviceClientFactory.CanHandle dispatches by protocol.
/// </summary>
public interface IThumbnailProvider
{
    /// <summary>Returns null if there is no current file, or the thumbnail couldn't be fetched.</summary>
    Task<PrintThumbnail?> GetThumbnailAsync(string fileName, CancellationToken cancellationToken);
}
