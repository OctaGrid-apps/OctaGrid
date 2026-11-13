using System.Text.Json;

namespace OctaGrid.Agent.Core.Extensions;

/// <summary>Remembers which extensions the user turned off and which installed extensions the user trusts.</summary>
public interface IExtensionStateStore
{
    /// <summary>Ids of disabled extensions. Empty when nothing was saved yet.</summary>
    IReadOnlySet<string> LoadDisabled();

    void SaveDisabled(IReadOnlyCollection<string> disabledIds);

    /// <summary>The version of each extension the user agreed to run, by extension id.</summary>
    IReadOnlyDictionary<string, string> LoadTrusted();

    void SaveTrusted(IReadOnlyDictionary<string, string> trusted);
}

/// <summary>Keeps the disabled and trusted extensions in a small JSON file.</summary>
public sealed class FileExtensionStateStore(string path) : IExtensionStateStore
{
    private sealed record State(List<string>? Disabled, Dictionary<string, string>? Trusted = null);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>A missing, empty or unreadable file means "nothing is disabled".</summary>
    public IReadOnlySet<string> LoadDisabled() =>
        new HashSet<string>(Read()?.Disabled ?? [], StringComparer.OrdinalIgnoreCase);

    public void SaveDisabled(IReadOnlyCollection<string> disabledIds) =>
        Write((Read() ?? new State(null)) with { Disabled = [.. disabledIds.Order()] });

    /// <summary>A missing or unreadable file means "nothing is trusted".</summary>
    public IReadOnlyDictionary<string, string> LoadTrusted() =>
        new Dictionary<string, string>(Read()?.Trusted ?? [], StringComparer.OrdinalIgnoreCase);

    public void SaveTrusted(IReadOnlyDictionary<string, string> trusted) =>
        Write((Read() ?? new State(null)) with { Trusted = new Dictionary<string, string>(trusted, StringComparer.OrdinalIgnoreCase) });

    private State? Read()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<State>(File.ReadAllText(path), JsonOptions) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void Write(State state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        // Write to a temporary file first so a crash cannot leave a half-written state file.
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }
}
