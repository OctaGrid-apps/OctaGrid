using OctaGrid.Protocol.Agents;
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

    Task<ConnectionTestResult> TestConnectionAsync(string protocol, string connectionString, string? apiKey, CancellationToken cancellationToken);

    Task TriggerInitialStateAsync(Guid printerId, CancellationToken cancellationToken);

    Task TriggerInitialStateAsync(IEnumerable<Guid> printers, CancellationToken cancellationToken);

    Task UploadFileAsync(Guid deviceId, Stream stream, string fileName, CancellationToken cancellationToken);
}
