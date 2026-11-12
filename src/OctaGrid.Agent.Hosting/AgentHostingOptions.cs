namespace OctaGrid.Agent.Hosting;

/// <summary>Where an agent keeps its files. Each host decides the locations.</summary>
/// <param name="ExtensionsFolder">The folder user-installed extensions are loaded from.</param>
/// <param name="ExtensionStatePath">The file that records which extensions are disabled or trusted.</param>
public sealed record AgentHostingOptions(string ExtensionsFolder, string ExtensionStatePath);
