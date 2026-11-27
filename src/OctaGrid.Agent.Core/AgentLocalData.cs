namespace OctaGrid.Agent.Core;

/// <summary>Where an agent keeps machine-local data that does not roam with the user.</summary>
public static class AgentLocalData
{
    /// <summary>
    /// <c>%LocalAppData%/OctaGrid.Data</c>. Not <c>%LocalAppData%/OctaGrid</c>, which the installer
    /// replaces on update and deletes on uninstall. Not roaming: the data is machine-specific, can be large
    /// and is rebuilt when lost.
    /// </summary>
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OctaGrid.Data");
}
