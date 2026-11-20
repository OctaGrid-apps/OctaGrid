using OctaGrid.Application.Services;
using OctaGrid.Client.Contracts;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Local;

/// <summary>
/// Which printers the UI shows, handed to <see cref="PrinterWatchService"/> as the local user. Local mode has one
/// client, so it is one watcher for as long as the app runs.
/// </summary>
internal sealed class LocalPrinterTelemetryClient : IPrinterTelemetryClient
{
    private readonly LocalCalls _calls;
    private readonly Guid _watcherId = Guid.NewGuid();

    public LocalPrinterTelemetryClient(LocalCalls calls)
    {
        _calls = calls;
    }

    public Task<IReadOnlyList<PrinterBackfill>> WatchAsync(
        IReadOnlyCollection<Guid> printerIds,
        IReadOnlyDictionary<Guid, DateTimeOffset>? resume,
        CancellationToken cancellationToken) =>
        _calls.Run<PrinterWatchService, IReadOnlyList<PrinterBackfill>>(
            (service, userId) => service.Watch(userId, _watcherId, printerIds, resume, cancellationToken), cancellationToken);
}
