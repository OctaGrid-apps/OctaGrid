using System.Reflection;

namespace OctaGrid.Client;

public static class Helper
{
    private const string DefaultVersion = "1.0.0";

    public static string LogsDirectory => Path.Combine(LocalDataFolder(), "logs");

    public static string GetAppVersion()
    {
        var infoVersion = GetInfoVersion();

        var plusIndex = infoVersion.IndexOf('+');
        var cleanVersion = plusIndex > 0 ? infoVersion[..plusIndex] : infoVersion;
        return $"v{cleanVersion}";
    }

    public static string GetInfoVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(Helper).Assembly;
        var infoVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        return string.IsNullOrWhiteSpace(infoVersion)
            ? $"v{assembly.GetName().Version?.ToString(3) ?? DefaultVersion}"
            : infoVersion;
    }

    public static string LocalDataFolder()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "OctaGrid");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
