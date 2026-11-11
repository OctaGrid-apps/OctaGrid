using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace OctaGrid.Data.Abstractions.Configurations;

/// <summary>Stores a list of strings as one JSON text column.</summary>
internal static class StringListJson
{
    public static readonly ValueConverter<List<string>, string> Converter = new(
        value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
        json => Deserialize(json));

    /// <summary>Compares lists by content, so changes are detected and snapshots are real copies.</summary>
    public static readonly ValueComparer<List<string>> Comparer = new(
        (a, b) => AreEqual(a, b),
        value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
        value => new List<string>(value));

    private static bool AreEqual(List<string>? a, List<string>? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        return a.SequenceEqual(b);
    }

    private static List<string> Deserialize(string json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<string>>(json, (JsonSerializerOptions?)null) ?? [];
}
