using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace OctaGrid.Data.Abstractions.Configurations;

/// <summary>Stores a string dictionary as one JSON text column.</summary>
internal static class DictionaryJson
{
    public static readonly ValueConverter<Dictionary<string, string>, string> Converter = new(
        value => Serialize(value),
        json => Deserialize(json));

    /// <summary>Compares dictionaries by content, so changes are detected and snapshots are real copies.</summary>
    public static readonly ValueComparer<Dictionary<string, string>> Comparer = new(
        (a, b) => AreEqual(a, b),
        value => Hash(value),
        value => new Dictionary<string, string>(value));

    private static bool AreEqual(Dictionary<string, string>? a, Dictionary<string, string>? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        return a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var other) && other == kv.Value);
    }

    private static int Hash(Dictionary<string, string> value) =>
        value.OrderBy(kv => kv.Key, StringComparer.Ordinal).Aggregate(0, (hash, kv) => HashCode.Combine(hash, kv.Key, kv.Value));

    private static string Serialize(Dictionary<string, string> value) =>
        JsonSerializer.Serialize(value, (JsonSerializerOptions?)null);

    private static Dictionary<string, string> Deserialize(string json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<Dictionary<string, string>>(json, (JsonSerializerOptions?)null) ?? [];
}
