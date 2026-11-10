using OctaGrid.Protocol.Agents;
using OctaGrid.Protocol.Extensions;
using OctaGrid.Protocol.Messaging;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Client.Contracts;

public interface IAgentCommandsService
{
    Task DeleteFileAsync(Guid deviceId, string fileName, CancellationToken cancellationToken);

    /// <summary>
    /// What happened to a printer, oldest first. Pass the sequence of the last event already shown as
    /// <paramref name="afterSequence"/> to get only what came after it, or null for the newest events. The page says
    /// when events were lost because the agent keeps a bounded history.
    /// </summary>
    Task<PrinterEventPage> GetPrinterEventsAsync(Guid printerId, long? afterSequence, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// What a printer wrote to its console, oldest first. Pass the sequence of the last line already shown as
    /// <paramref name="afterSequence"/> to get only what came after it, or null for the newest lines. Only printers whose
    /// connector declares <see cref="ConnectorCapabilities.Console"/> have any.
    /// </summary>
    Task<ConsolePage> GetPrinterConsoleAsync(Guid printerId, long? afterSequence, int limit, CancellationToken cancellationToken);

    Task<IEnumerable<PrintMetadata>> GetPrinterFilesAsync(Guid deviceId, CancellationToken cancellationToken);

    Task<PrintThumbnail?> GetThumbnailAsync(Guid printerId, string fileName, CancellationToken cancellationToken);

    Task PausePrintJobAsync(Guid deviceId, CancellationToken cancellationToken);

    Task ResumePrintJobAsync(Guid deviceId, CancellationToken cancellationToken);

    Task StartPrintJobAsync(Guid deviceId, string fileName, CancellationToken cancellationToken);

    Task StopPrintJobAsync(Guid deviceId, CancellationToken cancellationToken);

    /// <summary>The connectors available to add printers with, including their settings and capabilities.</summary>
    IReadOnlyList<ConnectorDescriptor> Connectors { get; }

    /// <param name="settings">The settings entered for the printer, including secrets, by the keys the connector declared.</param>
    Task<ConnectionTestResult> TestConnectionAsync(string connectorId, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken);

    Task TriggerInitialStateAsync(Guid printerId, CancellationToken cancellationToken);

    Task TriggerInitialStateAsync(IEnumerable<Guid> printers, CancellationToken cancellationToken);

    Task UploadFileAsync(Guid deviceId, Stream stream, string fileName, CancellationToken cancellationToken);
}
