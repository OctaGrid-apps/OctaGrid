namespace OctaGrid.Protocol.Printers;

/// <summary>
/// The current print job's thumbnail, as returned across the agent/client boundary
/// (in-process today; over HTTP/SignalR once Remote mode exists).
/// </summary>
public sealed record PrintThumbnail(string ContentType, byte[] Data);
