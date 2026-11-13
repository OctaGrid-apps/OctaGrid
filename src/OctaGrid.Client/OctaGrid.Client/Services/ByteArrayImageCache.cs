using Microsoft.Extensions.Caching.Memory;

namespace OctaGrid.Client.Services;

public sealed class ByteArrayImageCache : IByteArrayImageCache, IDisposable
{
    private readonly MemoryCache _cache;

    public ByteArrayImageCache(long maxCacheSizeBytes = 50 * 1024 * 1024)
    {
        _cache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = maxCacheSizeBytes,
            // Publishes hit/miss, eviction and size metrics on the built-in MemoryCache meter (.NET 11),
            // so an OpenTelemetry listener can see how well the image cache works.
            TrackStatistics = true,
            Name = "image-bytes"
        });
    }

    public void Add(string key, byte[] bytes)
    {
        if (string.IsNullOrWhiteSpace(key) || bytes == null || bytes.Length == 0)
        {
            return;
        }

        var options = new MemoryCacheEntryOptions()
            .SetSize(bytes.Length)
            .SetSlidingExpiration(TimeSpan.FromMinutes(15))
            .SetAbsoluteExpiration(TimeSpan.FromHours(2));

        _cache.Set(key, bytes, options);
    }

    /// <summary>
    /// Clears all entries from the cache.
    /// </summary>
    public void Clear() => _cache.Clear();

    public void Dispose() => _cache.Dispose();

    /// <summary>
    /// Removes a specific cached entry by its key.
    /// </summary>
    public void Remove(string key)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            _cache.Remove(key);
        }
    }

    public bool TryGetBytes(string key, out byte[]? bytes) => _cache.TryGetValue(key, out bytes);
}
