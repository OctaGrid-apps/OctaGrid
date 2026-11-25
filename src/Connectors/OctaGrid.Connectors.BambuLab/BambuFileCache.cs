using OctaGrid.Extensions.Models;

namespace OctaGrid.Connectors.BambuLab;

/// <summary>
/// What one session has already read from the printer's files, so a file is read over FTPS once and not again until it
/// changes. A file is the same while its name, size and modified time are; a listing drops the files that are gone.
/// </summary>
internal sealed class BambuFileCache
{
    private readonly Dictionary<string, Entry> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();
    private IReadOnlyList<string>? _listedNames;

    /// <summary>The names of the last listing, or null before the first one.</summary>
    public IReadOnlyList<string>? ListedNames
    {
        get
        {
            lock (_lock)
            {
                return _listedNames;
            }
        }
    }

    /// <summary>Finds a file read before, if it has not changed since.</summary>
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

    /// <summary>Finds a file read before by name alone, for requests that do not know its size.</summary>
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

    /// <summary>Records what a listing found and forgets the files it did not.</summary>
    public void SetListing(IReadOnlyList<string> names)
    {
        lock (_lock)
        {
            _listedNames = names;
            var present = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            foreach (var name in _files.Keys.Where(n => !present.Contains(n)).ToList())
            {
                _files.Remove(name);
            }
        }
    }

    /// <summary>
    /// Finds the file of a print job on the card. The printer reports the job by its project name ("rubber bolt"), and
    /// Bambu Studio names the files it exports "&lt;name&gt;.gcode.3mf" or "&lt;name&gt;_&lt;material&gt;_&lt;time&gt;.gcode.3mf".
    /// Returns null when no file or more than one could be the job's, so the card never shows another file's picture.
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

    /// <summary>A file as it was read: its size and time when read, what it says and its picture.</summary>
    internal sealed record Entry(long Size, DateTimeOffset? ModifiedAt, PrintMetadata Metadata, byte[]? Thumbnail);
}
