namespace OctaGrid.Data.Models;

/// <summary>
/// A printer's secrets in plain text, by the keys its connector declared. Immutable. Lives only in memory while a
/// service works with it: the printer stores them encrypted (<see cref="Printer.ProtectedSecrets"/>).
/// </summary>
public sealed class PrinterSecrets
{
    private readonly Dictionary<string, string> _values;

    public PrinterSecrets(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values = values
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    public static PrinterSecrets None { get; } = new(new Dictionary<string, string>());

    public bool IsEmpty => _values.Count == 0;

    /// <summary>The keys that have a value, in ordinal order: what a client may be told about them.</summary>
    public IReadOnlyList<string> Keys => _values.Keys.Order(StringComparer.Ordinal).ToList();

    public IReadOnlyDictionary<string, string> Values => _values;

    /// <summary>
    /// Applies what a user entered: a value replaces the saved one, an empty value removes it, and a key that is not
    /// in <paramref name="changes"/> keeps its saved value (secrets are write-only, so a form never shows them).
    /// </summary>
    public PrinterSecrets With(IReadOnlyDictionary<string, string>? changes)
    {
        if (changes is null || changes.Count == 0)
        {
            return this;
        }

        var result = new Dictionary<string, string>(_values);
        foreach (var (key, value) in changes)
        {
            if (string.IsNullOrEmpty(value))
            {
                result.Remove(key);
            }
            else
            {
                result[key] = value;
            }
        }

        return new PrinterSecrets(result);
    }
}
