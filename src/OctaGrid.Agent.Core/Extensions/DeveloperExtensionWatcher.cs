namespace OctaGrid.Agent.Core.Extensions;

/// <summary>
/// Watches the developer folder and calls back once a build has finished writing: changes are collected until the
/// folder has been quiet for <c>debounce</c>, because a build writes several files one after another.
/// </summary>
public sealed class DeveloperExtensionWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _timer;
    private readonly TimeSpan _debounce;
    private readonly Action _onChanged;
    private bool _disposed;

    /// <param name="folder">The developer folder.</param>
    /// <param name="onChanged">Called (on a thread-pool thread) after the folder went quiet.</param>
    /// <param name="debounce">How long the folder must be quiet; 750 ms when null.</param>
    public DeveloperExtensionWatcher(string folder, Action onChanged, TimeSpan? debounce = null)
    {
        _onChanged = onChanged;
        _debounce = debounce ?? TimeSpan.FromMilliseconds(750);
        _timer = new Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);

        _watcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            InternalBufferSize = 64 * 1024,
        };
        _watcher.Changed += OnEvent;
        _watcher.Created += OnEvent;
        _watcher.Deleted += OnEvent;
        _watcher.Renamed += OnEvent;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnEvent(object sender, FileSystemEventArgs e)
    {
        // Only files that make up an extension; symbols, logs and temporary files do not matter.
        var extension = Path.GetExtension(e.Name);
        if (extension is not (".dll" or ".json"))
        {
            return;
        }

        if (!_disposed)
        {
            _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void Fire()
    {
        if (!_disposed)
        {
            _onChanged();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _watcher.Dispose();
        _timer.Dispose();
    }
}
