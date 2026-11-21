using OctaGrid.Client.Contracts;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.DesignTime;

/// <summary>A telemetry client for the designer: watching changes nothing and returns no state, so cards keep their design data.</summary>
internal sealed class DesignPrinterTelemetryClient : IPrinterTelemetryClient
{
    public Task<IReadOnlyList<PrinterBackfill>> WatchAsync(
        IReadOnlyCollection<Guid> printerIds,
        IReadOnlyDictionary<Guid, DateTimeOffset>? resume,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PrinterBackfill>>([]);
}
