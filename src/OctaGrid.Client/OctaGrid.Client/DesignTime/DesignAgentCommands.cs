using OctaGrid.Client.Contracts;
using OctaGrid.Protocol.Agents;
using OctaGrid.Protocol.Extensions;
using OctaGrid.Protocol.Messaging;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Client.DesignTime;

/// <summary>Agent commands that do nothing, for the designer and for tests.</summary>
internal sealed class DesignAgentCommands : IAgentCommandsService
{
    private static readonly Guid DesignHistoryId = Guid.NewGuid();

    public Task DeleteFileAsync(Guid deviceId, string fileName, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<ConsolePage> GetPrinterConsoleAsync(Guid printerId, long? afterSequence, int limit, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.Now;
        ConsoleLine[] lines =
        [
            new(1, now.AddSeconds(-12), "ok"),
            new(2, now.AddSeconds(-8), "T:215.3 /215.0 B:59.8 /60.0"),
            new(3, now.AddSeconds(-4), "// Klipper state: Ready"),
        ];
        return Task.FromResult(new ConsolePage(lines.Where(l => l.Sequence > (afterSequence ?? 0)).ToList(), 3, false, DesignHistoryId));
    }

    public Task<PrinterEventPage> GetPrinterEventsAsync(Guid printerId, long? afterSequence, int limit, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.Now;
        PrinterEvent[] events =
        [
            new(1, now.AddMinutes(-42), PrinterSeverity.Info, "state.changed", "State changed to Printing."),
            new(2, now.AddMinutes(-12), PrinterSeverity.Warning, "filament.low", "Filament is running low."),
            new(3, now.AddMinutes(-3), PrinterSeverity.Info, "filament.low", "Resolved: Filament is running low.")
        ];
        return Task.FromResult(new PrinterEventPage(events.Where(e => e.Sequence > (afterSequence ?? 0)).ToList(), 3, false, DesignHistoryId));
    }

    public Task<IEnumerable<PrintMetadata>> GetPrinterFilesAsync(Guid deviceId, CancellationToken cancellationToken) =>
        Task.FromResult<IEnumerable<PrintMetadata>>([]);

    public Task<PrintThumbnail?> GetThumbnailAsync(Guid printerId, string fileName, CancellationToken cancellationToken) =>
                Task.FromResult<PrintThumbnail?>(null);

    public Task PausePrintJobAsync(Guid deviceId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task ResumePrintJobAsync(Guid deviceId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task StartPrintJobAsync(Guid deviceId, string fileName, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task StopPrintJobAsync(Guid deviceId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public IReadOnlyList<ConnectorDescriptor> Connectors { get; } = [];

    public Task<ConnectionTestResult> TestConnectionAsync(string connectorId, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken) =>
        Task.FromResult(new ConnectionTestResult(true, null));

    public Task TriggerInitialStateAsync(Guid printerId, CancellationToken cancellationToken) =>
                            Task.CompletedTask;

    public Task TriggerInitialStateAsync(IEnumerable<Guid> printers, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task UploadFileAsync(Guid deviceId, Stream stream, string fileName, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
