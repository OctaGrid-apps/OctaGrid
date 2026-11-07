namespace OctaGrid.Client.Contracts;

public static class OctaGridDataPaths
{
    private static readonly Lazy<string> LazyDataFolder = new(InitializeDataFolder);

    public static string DataFolder => LazyDataFolder.Value;

    public static string DatabasePath => Path.Combine(DataFolder, "octagrid.db");

    public static string SettingsPath => Path.Combine(DataFolder, "octaGrid.config.json");

    public static string LogsFolder => Path.Combine(DataFolder, "logs");

    /// <summary>Where users put extensions: <c>&lt;id&gt;/&lt;version&gt;/octagrid-extension.json</c> or a developer's build folder.</summary>
    public static string ExtensionsFolder => Path.Combine(DataFolder, "extensions");

    private static string InitializeDataFolder()
    {
        var targetDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create),
            "OctaGrid");

        Directory.CreateDirectory(targetDir);
        return targetDir;
    }
}
