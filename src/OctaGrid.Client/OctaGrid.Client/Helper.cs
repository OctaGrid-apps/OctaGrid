using System.Reflection;
using System.Text.RegularExpressions;
using OctaGrid.Client.Contracts;

namespace OctaGrid.Client;

public static partial class Helper
{
    private const string DefaultVersion = "1.0.0";

    // major.minor.patch, an optional fourth number (the git height Nerdbank.GitVersioning adds), an optional
    // pre-release label and optional build metadata.
    [GeneratedRegex(@"^v?(\d+\.\d+\.\d+)(?:\.\d+)?(-[0-9A-Za-z.-]+)?(?:\+.*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    public static string LogsDirectory => OctaGridDataPaths.LogsFolder;

    /// <summary>The version shown to users, for example <c>v1.0.20-pre-alpha</c>: the same as the release tag.</summary>
    public static string GetAppVersion() => $"v{ToReleaseVersion(GetInfoVersion())}";

    /// <summary>
    /// Turns the version stamped into the assembly (<c>1.0.20.1-pre-alpha+4f6d5bb</c>) into the version of the release
    /// (<c>1.0.20-pre-alpha</c>): the fourth number is the git height and the part after <c>+</c> is the commit.
    /// </summary>
    public static string ToReleaseVersion(string version)
    {
        var match = VersionPattern().Match(version.Trim());
        if (match.Success)
        {
            return match.Groups[1].Value + match.Groups[2].Value;
        }

        var plus = version.IndexOf('+');
        return plus > 0 ? version[..plus] : version;
    }

    /// <summary>The full version stamped into the assembly, including the git height and the commit.</summary>
    public static string GetInfoVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(Helper).Assembly;
        var infoVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        return string.IsNullOrWhiteSpace(infoVersion)
            ? assembly.GetName().Version?.ToString(3) ?? DefaultVersion
            : infoVersion;
    }

    public static string LocalDataFolder() => OctaGridDataPaths.DataFolder;
}
