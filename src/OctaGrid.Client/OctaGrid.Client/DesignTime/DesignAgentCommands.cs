using OctaGrid.Client.Contracts;
using OctaGrid.Extensions.Models;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.DesignTime;

/// <summary>Agent commands that do nothing, for the designer and for tests.</summary>
internal sealed class DesignAgentCommands : IAgentCommandsService
{
    private static readonly Guid DesignHistoryId = Guid.NewGuid();

    public Task DeleteFileAsync(Guid printerId, string fileName, CancellationToken cancellationToken) =>
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

    public Task<IEnumerable<PrintMetadata>> GetPrinterFilesAsync(Guid printerId, CancellationToken cancellationToken) =>
        Task.FromResult<IEnumerable<PrintMetadata>>([]);

    public Task<IReadOnlyList<PrintMetadata>> GetPrinterFileDetailsAsync(Guid printerId, IReadOnlyList<string> fileNames, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PrintMetadata>>([]);

    public Task<PrintThumbnail?> GetThumbnailAsync(Guid printerId, string fileName, CancellationToken cancellationToken) =>
                Task.FromResult<PrintThumbnail?>(null);

    public Task PausePrintJobAsync(Guid printerId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task ResumePrintJobAsync(Guid printerId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task SendConsoleCommandAsync(Guid printerId, string command, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<PrintStartPlan?> GetPrintStartPlanAsync(Guid printerId, string fileName, CancellationToken cancellationToken) =>
        Task.FromResult<PrintStartPlan?>(DesignPlan(fileName));

    /// <summary>A file with two plates, one of which cannot start, and the Bambu start options.</summary>
    public static PrintStartPlan DesignPlan(string fileName) => new(
        fileName,
        [
            new PrintPlate(1, "Benchy", 2460, 15.3, [new PlateFilament("PLA", "FFFFFF", "AMS A1"), new PlateFilament("PLA", "D32F2F", "AMS A3")]),
            new PrintPlate(2, null, 5400, 41.0, [new PlateFilament("PETG", "000000")], Problem: "No loaded spool matches PETG #000000."),
        ],
        [
            new SettingField("bed_levelling", "Bed levelling", SettingFieldType.Boolean, Required: false, DefaultValue: "true"),
            new SettingField("flow_cali", "Flow calibration", SettingFieldType.Boolean, Required: false, DefaultValue: "true"),
            new SettingField("timelapse", "Timelapse", SettingFieldType.Boolean, Required: false, DefaultValue: "false"),
        ]);

    public Task StartPrintJobAsync(Guid printerId, string fileName, PrintStartChoices? choices, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task StopPrintJobAsync(Guid printerId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<ConnectorDescriptor>> GetConnectorsAsync(Guid agentId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ConnectorDescriptor>>([]);

    public Task<ConnectionTestResult> TestConnectionAsync(Guid workspaceId, string connectorId, IReadOnlyDictionary<string, string> settings, Guid? printerId, Guid? agentId, CancellationToken cancellationToken) =>
        Task.FromResult(new ConnectionTestResult(true, null));

    public Task UploadFileAsync(Guid printerId, Stream stream, string fileName, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
