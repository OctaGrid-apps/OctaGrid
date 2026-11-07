namespace OctaGrid.Client.Services;

/// <summary>Works out which folder developer mode loads extensions from at startup.</summary>
public static class DeveloperMode
{
    /// <summary>Command-line option: <c>--extensions-dev &lt;folder&gt;</c> or <c>--extensions-dev=&lt;folder&gt;</c>.</summary>
    public const string Option = "--extensions-dev";

    /// <summary>Environment variable that holds the folder, for launch profiles and scripts.</summary>
    public const string EnvironmentVariable = "OCTAGRID_EXTENSIONS_DEV";

    /// <summary>
    /// The folder to use, from the first of: the command line, the environment variable, the folder saved in the
    /// settings. Null when none is given.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="savedFolder">The folder saved in the settings.</param>
    /// <param name="getEnvironmentVariable">Reads an environment variable; the process environment when null.</param>
    public static string? ResolveFolder(
        IReadOnlyList<string>? args,
        string? savedFolder,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        if (FromCommandLine(args) is { } fromCommandLine)
        {
            return fromCommandLine;
        }

        var fromEnvironment = (getEnvironmentVariable ?? Environment.GetEnvironmentVariable)(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment.Trim();
        }

        return string.IsNullOrWhiteSpace(savedFolder) ? null : savedFolder;
    }

    private static string? FromCommandLine(IReadOnlyList<string>? args)
    {
        if (args is null)
        {
            return null;
        }

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, Option, StringComparison.OrdinalIgnoreCase))
            {
                return i + 1 < args.Count && !string.IsNullOrWhiteSpace(args[i + 1]) ? args[i + 1] : null;
            }

            if (arg.StartsWith(Option + "=", StringComparison.OrdinalIgnoreCase))
            {
                var value = arg[(Option.Length + 1)..].Trim('"');
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        return null;
    }
}
