using System.Text.RegularExpressions;

namespace OctaGrid.Extensions.Models;

/// <summary>
/// Rules for extension ids. An id is <c>publisher.name</c>: two lowercase segments of letters,
/// digits and single hyphens, for example <c>octagrid.moonraker</c> or <c>acme.prusa-link</c>.
/// Ids never change once published. A connector uses the id of the extension that provides it
/// (<c>ConnectorDescriptor.Id</c> equals the manifest id), so this one rule covers both.
/// </summary>
public static partial class ExtensionId
{
    public const int MaxLength = 100;

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*\.[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static bool IsValid(string? id) =>
        id is { Length: > 0 and <= MaxLength } && Pattern().IsMatch(id);
}
