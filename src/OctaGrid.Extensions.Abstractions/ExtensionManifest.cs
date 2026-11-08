using System.Text.Json;
using System.Text.RegularExpressions;
using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Extensions.Abstractions;

/// <summary>Known values of <see cref="ExtensionManifest.Kind"/>.</summary>
public static class ExtensionKinds
{
    /// <summary>A printer connector (an <see cref="IConnector"/> implementation).</summary>
    public const string Connector = "connector";
}

/// <summary>
/// The <c>octagrid-extension.json</c> file at the root of every extension package. It is read before any
/// extension code is loaded, so it must be enough to decide whether to load the extension at all.
/// </summary>
/// <param name="Id">Extension id, <c>publisher.name</c> (see <see cref="ExtensionId"/>).</param>
/// <param name="Name">Name shown to users.</param>
/// <param name="Version">Version of the extension (SemVer).</param>
/// <param name="Publisher">Publisher shown to users.</param>
/// <param name="Kind">Kind of extension; see <see cref="ExtensionKinds"/>.</param>
/// <param name="Host">
/// Range of OctaGrid versions the extension works with, for example <c>&gt;=1.1.0 &lt;2.0.0</c>.
/// The host evaluates it; this type only checks that it is present.
/// </param>
/// <param name="Assembly">Extension assembly to load in-process, relative to the package root.</param>
/// <param name="EntryType">Full name of the <see cref="IConnector"/> implementation inside <paramref name="Assembly"/>.</param>
/// <param name="Alias">Execution alias of an out-of-process extension (Microsoft Store packages).</param>
/// <param name="Permissions">
/// What the extension says it needs, for example <c>network</c> or <c>files</c>. The host shows them when it asks the
/// user to trust the extension. They are a declaration, not a sandbox: an in-process extension has the host's permissions.
/// </param>
/// <param name="ProjectUrl">
/// Optional web page of the extension (its repository or home page), shown as a link in the extension list.
/// It must be an absolute <c>http</c> or <c>https</c> address.
/// </param>
public sealed record ExtensionManifest(
    string Id,
    string Name,
    string Version,
    string Publisher,
    string Kind,
    string Host,
    string? Assembly = null,
    string? EntryType = null,
    string? Alias = null,
    IReadOnlyList<string>? Permissions = null,
    string? ProjectUrl = null)
{
    /// <summary>File name of the manifest inside an extension package.</summary>
    public const string FileName = "octagrid-extension.json";

    private static readonly Regex SemVer = new(
        @"^\d+\.\d+\.\d+(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?(\+[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$",
        RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Reads a manifest from JSON. Unknown properties are ignored; call <see cref="Validate"/> afterwards.</summary>
    /// <exception cref="JsonException">The text is not valid JSON or not a manifest object.</exception>
    public static ExtensionManifest Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize<ExtensionManifest>(json, JsonOptions)
            ?? throw new JsonException("The manifest is empty.");
    }

    /// <summary>Checks the manifest and returns one message per problem; an empty list means it is valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (!ExtensionId.IsValid(Id))
        {
            errors.Add($"'id' must look like 'publisher.name' (lowercase letters, digits, hyphens); got '{Id}'.");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add("'name' is required.");
        }

        if (string.IsNullOrWhiteSpace(Publisher))
        {
            errors.Add("'publisher' is required.");
        }

        if (string.IsNullOrWhiteSpace(Version) || !SemVer.IsMatch(Version))
        {
            errors.Add($"'version' must be a SemVer version such as '1.2.0' or '1.2.0-preview.1'; got '{Version}'.");
        }

        if (Kind != ExtensionKinds.Connector)
        {
            errors.Add($"'kind' must be '{ExtensionKinds.Connector}'; got '{Kind}'.");
        }

        if (string.IsNullOrWhiteSpace(Host))
        {
            errors.Add("'host' (the supported OctaGrid versions) is required.");
        }

        var inProcess = !string.IsNullOrWhiteSpace(Assembly) && !string.IsNullOrWhiteSpace(EntryType);
        var outOfProcess = !string.IsNullOrWhiteSpace(Alias);
        if (!inProcess && !outOfProcess)
        {
            errors.Add("Either 'assembly' with 'entryType' (in-process) or 'alias' (out-of-process) is required.");
        }

        if (!string.IsNullOrWhiteSpace(Assembly) && (Path.IsPathRooted(Assembly) || Assembly.Contains("..")))
        {
            errors.Add("'assembly' must be a relative path inside the package.");
        }

        if (!string.IsNullOrWhiteSpace(ProjectUrl) && !IsWebAddress(ProjectUrl))
        {
            errors.Add($"'projectUrl' must be an absolute http or https address; got '{ProjectUrl}'.");
        }

        return errors;
    }

    /// <summary>True when <paramref name="value"/> is an absolute <c>http</c> or <c>https</c> address.</summary>
    public static bool IsWebAddress(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
}
