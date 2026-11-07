using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.RegularExpressions;

namespace OctaGrid.Agent.Core.Connectors;

/// <summary>A SemVer 2.0 version with the precedence rules the host needs to check <see cref="HostVersionRange"/>s.</summary>
public sealed partial class ExtensionVersion : IComparable<ExtensionVersion>, IEquatable<ExtensionVersion>
{
    [GeneratedRegex(@"^(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    private ExtensionVersion(int major, int minor, int patch, string? preRelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = preRelease;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    /// <summary>The part after the hyphen, for example <c>pre-alpha.5</c>; null for a stable version.</summary>
    public string? PreRelease { get; }

    public bool IsPreRelease => PreRelease is not null;

    /// <summary>The version of the running host, read from the assembly's informational version.</summary>
    public static ExtensionVersion Host { get; } = ReadHostVersion();

    /// <summary>Parses <c>1.2.3</c>, <c>1.2.3-preview.1</c> and <c>1.2.3+build</c> (build metadata is ignored).</summary>
    public static bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out ExtensionVersion? version)
    {
        version = null;
        if (text is null || Pattern().Match(text.Trim()) is not { Success: true } match)
        {
            return false;
        }

        if (!int.TryParse(match.Groups[1].Value, out var major)
            || !int.TryParse(match.Groups[2].Value, out var minor)
            || !int.TryParse(match.Groups[3].Value, out var patch))
        {
            return false;
        }

        version = new ExtensionVersion(major, minor, patch, match.Groups[4].Success ? match.Groups[4].Value : null);
        return true;
    }

    public static ExtensionVersion Parse(string text) =>
        TryParse(text, out var version) ? version : throw new FormatException($"'{text}' is not a SemVer version.");

    /// <summary>True when both versions have the same major, minor and patch numbers.</summary>
    public bool SameCore(ExtensionVersion other) =>
        Major == other.Major && Minor == other.Minor && Patch == other.Patch;

    public int CompareTo(ExtensionVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var core = Major.CompareTo(other.Major);
        if (core == 0)
        {
            core = Minor.CompareTo(other.Minor);
        }

        if (core == 0)
        {
            core = Patch.CompareTo(other.Patch);
        }

        return core != 0 ? core : ComparePreRelease(PreRelease, other.PreRelease);
    }

    public bool Equals(ExtensionVersion? other) => other is not null && CompareTo(other) == 0;

    public override bool Equals(object? obj) => Equals(obj as ExtensionVersion);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, PreRelease);

    public override string ToString() => PreRelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{PreRelease}";

    // A version with a pre-release label sorts before the same version without one; labels compare
    // identifier by identifier, numbers numerically and before text (SemVer 2.0, section 11).
    private static int ComparePreRelease(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return left is null ? (right is null ? 0 : 1) : -1;
        }

        var a = left.Split('.');
        var b = right.Split('.');
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var aNumeric = long.TryParse(a[i], out var aNumber);
            var bNumeric = long.TryParse(b[i], out var bNumber);
            var result = (aNumeric, bNumeric) switch
            {
                (true, true) => aNumber.CompareTo(bNumber),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(a[i], b[i]),
            };

            if (result != 0)
            {
                return result;
            }
        }

        return a.Length.CompareTo(b.Length);
    }

    [GeneratedRegex(@"^(\d+)\.(\d+)\.(\d+)(?:\.(\d+))?(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+.*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex AssemblyVersionPattern();

    /// <summary>
    /// Reads the version stamped into an assembly, for example <c>1.0.19.4-pre-alpha+e75a240c04</c>. That text has a
    /// fourth number (the git height) that is not SemVer: on a pre-release it becomes part of the label
    /// (<c>1.0.19-pre-alpha.4</c>) and on a stable version it is dropped. Returns 0.0.0 when the text cannot be read.
    /// </summary>
    public static ExtensionVersion FromAssemblyVersion(string? text)
    {
        if (text is not null && AssemblyVersionPattern().Match(text.Trim()) is { Success: true } match
            && int.TryParse(match.Groups[1].Value, out var major)
            && int.TryParse(match.Groups[2].Value, out var minor)
            && int.TryParse(match.Groups[3].Value, out var patch))
        {
            var label = match.Groups[5].Success ? match.Groups[5].Value : null;
            if (label is not null && match.Groups[4].Success)
            {
                label += "." + match.Groups[4].Value;
            }

            return new ExtensionVersion(major, minor, patch, label);
        }

        return new ExtensionVersion(0, 0, 0, null);
    }

    private static ExtensionVersion ReadHostVersion() =>
        FromAssemblyVersion(typeof(ExtensionVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
}

/// <summary>
/// The <c>host</c> range of an extension manifest, for example <c>&gt;=1.1.0 &lt;2.0.0</c>: comparators
/// separated by spaces, all of which must hold. A comparator is <c>&gt;=</c>, <c>&gt;</c>, <c>&lt;=</c>,
/// <c>&lt;</c> or <c>=</c> (or nothing) followed by a version; <c>*</c> accepts any version.
/// </summary>
/// <remarks>
/// A pre-release host (<c>1.0.19-pre-alpha.3</c>) satisfies a range only when the range names a pre-release of
/// the same <c>major.minor.patch</c>, for example <c>&gt;=1.0.19-pre-alpha</c>.
/// </remarks>
public sealed class HostVersionRange
{
    private readonly List<(string Operator, ExtensionVersion Version)> _comparators;

    private HostVersionRange(string text, List<(string Operator, ExtensionVersion Version)> comparators)
    {
        Text = text;
        _comparators = comparators;
    }

    /// <summary>The range as written in the manifest.</summary>
    public string Text { get; }

    public static bool TryParse(string? text, [NotNullWhen(true)] out HostVersionRange? range, [NotNullWhen(false)] out string? error)
    {
        range = null;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "The host version range is empty.";
            return false;
        }

        var comparators = new List<(string, ExtensionVersion)>();
        foreach (var token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token == "*")
            {
                continue;
            }

            var op = token switch
            {
                _ when token.StartsWith(">=", StringComparison.Ordinal) => ">=",
                _ when token.StartsWith("<=", StringComparison.Ordinal) => "<=",
                _ when token.StartsWith('>') => ">",
                _ when token.StartsWith('<') => "<",
                _ when token.StartsWith('=') => "=",
                _ => "=",
            };

            var versionText = token.Length > 0 && (token[0] is '>' or '<' or '=') ? token[op.Length..] : token;
            if (!ExtensionVersion.TryParse(versionText, out var version))
            {
                error = $"'{token}' is not a version comparator such as '>=1.1.0'.";
                return false;
            }

            comparators.Add((op, version));
        }

        range = new HostVersionRange(text.Trim(), comparators);
        return true;
    }

    /// <summary>True when <paramref name="host"/> is inside the range.</summary>
    public bool Satisfies(ExtensionVersion host)
    {
        if (host.IsPreRelease && !_comparators.Any(c => c.Version.IsPreRelease && c.Version.SameCore(host)))
        {
            return false;
        }

        foreach (var (op, version) in _comparators)
        {
            var compare = host.CompareTo(version);
            var ok = op switch
            {
                ">=" => compare >= 0,
                ">" => compare > 0,
                "<=" => compare <= 0,
                "<" => compare < 0,
                _ => compare == 0,
            };

            if (!ok)
            {
                return false;
            }
        }

        return true;
    }
}
