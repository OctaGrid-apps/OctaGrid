using System.Reflection;

namespace OctaGrid.Connectors.BambuLab;

internal static class ConnectorVersion
{
    /// <summary>The version of the assembly that contains <paramref name="type"/>, without build metadata.</summary>
    public static string Current(Type type)
    {
        var info = type.Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(info))
        {
            return "0.0.0";
        }

        var plus = info.IndexOf('+');
        return plus > 0 ? info[..plus] : info;
    }
}
