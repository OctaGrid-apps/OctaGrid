using OctaGrid.Extensions.Models;

namespace OctaGrid.Connectors.BambuLab;

/// <summary>
/// Files a session has read over FTPS, keyed by name. An entry is valid while the file's size and
/// modified time are unchanged; a listing drops the entries of files that are gone.
/// </summary>
internal sealed class BambuFileCache
{
    private readonly Dictionary<string, Entry> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    /// <summary>Finds a file's entry if its size and modified time match.</summary>
    public bool TryGet(string name, long size, DateTimeOffset? modifiedAt, out Entry entry)
    {
        lock (_lock)
        {
            if (_files.TryGetValue(name, out var found) && found.Size == size && found.ModifiedAt == modifiedAt)
            {
                entry = found;
                return true;
            }
        }

        entry = null!;
        return false;
    }

    /// <summary>Finds a file's entry by name alone, without checking that it is current.</summary>
    public bool TryGet(string name, out Entry entry)
    {
        lock (_lock)
        {
            return _files.TryGetValue(name, out entry!);
        }
    }

    public void Store(string name, Entry entry)
    {
        lock (_lock)
        {
            _files[name] = entry;
        }
    }

    /// <summary>Drops the entries of files not in <paramref name="names"/>.</summary>
    public void SetListing(IReadOnlyList<string> names)
    {
        lock (_lock)
        {
            var present = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            foreach (var name in _files.Keys.Where(n => !present.Contains(n)).ToList())
            {
                _files.Remove(name);
            }
        }
    }

    /// <summary>
    /// Finds the file of a print job on the card. The printer reports the job by its project name,
    /// and Bambu Studio exports it as "&lt;name&gt;.gcode.3mf" or
    /// "&lt;name&gt;_&lt;material&gt;_&lt;time&gt;.gcode.3mf".
    /// Returns null when no file, or more than one, matches.
    /// </summary>
    public static string? ResolveJobFile(string jobName, IEnumerable<string> fileNames)
    {
        if (string.IsNullOrWhiteSpace(jobName))
        {
            return null;
        }

        var names = fileNames as IReadOnlyCollection<string> ?? fileNames.ToList();
        string[] exact = [jobName, jobName + ".gcode.3mf", jobName + ".3mf"];
        foreach (var candidate in exact)
        {
            var match = names.FirstOrDefault(n => string.Equals(n, candidate, StringComparison.OrdinalIgnoreCase));
            if (match is not null && match.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase))
            {
                return match;
            }
        }

        var prefixed = names
            .Where(n => n.StartsWith(jobName + "_", StringComparison.OrdinalIgnoreCase) &&
                        n.EndsWith(".gcode.3mf", StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();
        return prefixed.Count == 1 ? prefixed[0] : null;
    }

    /// <summary>A file as read: its size and modified time at the read, metadata and thumbnail.</summary>
    internal sealed record Entry(long Size, DateTimeOffset? ModifiedAt, PrintMetadata Metadata, byte[]? Thumbnail);
}
