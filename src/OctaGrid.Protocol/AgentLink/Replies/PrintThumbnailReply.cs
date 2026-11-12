using OctaGrid.Extensions.Models;

namespace OctaGrid.Protocol.AgentLink.Replies;

/// <summary>The preview image of a file.</summary>
/// <param name="Thumbnail">The image, or null when the file has none.</param>
public sealed record PrintThumbnailReply(PrintThumbnail? Thumbnail) : AgentReply;
