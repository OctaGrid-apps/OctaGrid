using System.Text.Json;

namespace OctaGrid.Agent.Core.Connectors;

/// <summary>Remembers which extensions the user turned off.</summary>
public interface IExtensionStateStore
{
    /// <summary>Ids of disabled extensions. Empty when nothing was saved yet.</summary>
    IReadOnlySet<string> LoadDisabled();

    void SaveDisabled(IReadOnlyCollection<string> disabledIds);
}

/// <summary>Keeps the disabled extensions in a small JSON file.</summary>
public sealed class FileExtensionStateStore(string path) : IExtensionStateStore
{
    private sealed record State(List<string>? Disabled);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>A missing, empty or unreadable file means "nothing is disabled".</summary>
    public IReadOnlySet<string> LoadDisabled()
    {
        try
        {
            if (!File.Exists(path))
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var state = JsonSerializer.Deserialize<State>(File.ReadAllText(path), JsonOptions);
            return new HashSet<string>(state?.Disabled ?? [], StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void SaveDisabled(IReadOnlyCollection<string> disabledIds)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        // Write to a temporary file first so a crash cannot leave a half-written state file.
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new State([.. disabledIds.Order()]), JsonOptions));
        File.Move(temp, path, overwrite: true);
    }
}
