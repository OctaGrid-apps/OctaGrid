namespace OctaGrid.Agent.Hosting;

/// <summary>Where an agent keeps its files. Each host decides the locations.</summary>
/// <param name="ExtensionsFolder">The folder user-installed extensions are loaded from.</param>
/// <param name="ExtensionStatePath">The file that records which extensions are disabled or trusted.</param>
/// <param name="UploadsFolder">
/// Where files sent to the agent are kept until they are used. Null puts them in the system's temporary folder.
/// </param>
/// <param name="FileCacheFolder">
/// Where printer file details and thumbnails are cached across restarts. Each agent on a machine needs its
/// own folder, because the cache deletes entries of printers that are not the agent's. Null uses
/// <c>%LocalAppData%/OctaGrid.Data/file-cache</c>.
/// </param>
public sealed record AgentHostingOptions(
    string ExtensionsFolder,
    string ExtensionStatePath,
    string? UploadsFolder = null,
    string? FileCacheFolder = null);
