namespace OctaGrid.Agent.Core;

/// <summary>Where an agent keeps data that belongs to this machine and does not roam with the user.</summary>
public static class AgentLocalData
{
    /// <summary>
    /// <c>%LocalAppData%/OctaGrid.Data</c>. Not <c>%LocalAppData%/OctaGrid</c>: the desktop installer owns that
    /// folder and replaces or deletes it on update and uninstall. Not the roaming folder either: this data (connector
    /// files, the file cache) describes this machine's printers, can be large and is rebuilt when lost.
    /// </summary>
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OctaGrid.Data");
}
