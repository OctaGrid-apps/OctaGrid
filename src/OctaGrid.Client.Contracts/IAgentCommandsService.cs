using OctaGrid.Protocol.Agents;
using OctaGrid.Protocol.Extensions;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Client.Contracts;

public interface IAgentCommandsService
{
    Task DeleteFileAsync(Guid deviceId, string fileName, CancellationToken cancellationToken);

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
