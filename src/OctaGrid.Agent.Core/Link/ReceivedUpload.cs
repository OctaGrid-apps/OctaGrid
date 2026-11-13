using Microsoft.Extensions.Logging;

namespace OctaGrid.Agent.Core.Link;

/// <summary>A finished upload: a temporary file that is deleted when this is disposed.</summary>
public sealed class ReceivedUpload : IDisposable
{
    private readonly ILogger _logger;

    internal ReceivedUpload(string path, long length, ILogger logger)
    {
        Path = path;
        Length = length;
        _logger = logger;
    }

    /// <summary>The size in bytes.</summary>
    public long Length { get; }

    /// <summary>The temporary file that holds the content.</summary>
    public string Path { get; }

    /// <summary>Opens the content for reading.</summary>
    public Stream OpenRead() =>
        new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

    public void Dispose()
    {
        try
        {
            File.Delete(Path);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not delete the temporary file of a finished upload");
        }
    }
}
