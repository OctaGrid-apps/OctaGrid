namespace OctaGrid.Client.Contracts;

public static class OctaGridDataPaths
{
    private static readonly Lazy<string> LazyDataFolder = new(InitializeDataFolder);

    public static string DataFolder => LazyDataFolder.Value;

    public static string DatabasePath => Path.Combine(DataFolder, "octagrid.db");

    public static string SettingsPath => Path.Combine(DataFolder, "octaGrid.config.json");

    public static string LogsFolder => Path.Combine(DataFolder, "logs");

    private static string InitializeDataFolder()
    {
        var targetDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create),
            "OctaGrid");

        Directory.CreateDirectory(targetDir);
        return targetDir;
    }
}
