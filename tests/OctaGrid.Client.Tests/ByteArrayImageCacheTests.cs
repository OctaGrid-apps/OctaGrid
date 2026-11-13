using System.Diagnostics.Metrics;
using OctaGrid.Client.Services;

namespace OctaGrid.Client.Tests;

public class ByteArrayImageCacheTests
{
    [Fact]
    public void TryGetBytes_AfterAdd_ReturnsStoredBytes()
    {
        using var cache = new ByteArrayImageCache();
        cache.Add("a", [1, 2, 3]);

        Assert.True(cache.TryGetBytes("a", out var bytes));
        Assert.Equal(new byte[] { 1, 2, 3 }, bytes);
        Assert.False(cache.TryGetBytes("missing", out _));
    }

    [Fact]
    public void Lookups_ArePublishedOnTheMemoryCacheMeter()
    {
        using var cache = new ByteArrayImageCache();
        cache.Add("a", [1, 2, 3]);
        cache.TryGetBytes("a", out _);
        cache.TryGetBytes("missing", out _);

        var requests = 0L;
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == "Microsoft.Extensions.Caching.Memory.MemoryCache"
                    && instrument.Name == "dotnet.cache.requests")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => requests += value);
        listener.Start();
        listener.RecordObservableInstruments();

        Assert.True(requests >= 2, $"Expected hit and miss requests to be reported, got {requests}.");
    }
}
