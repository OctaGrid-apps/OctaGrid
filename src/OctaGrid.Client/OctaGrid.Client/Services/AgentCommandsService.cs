using OctaGrid.Client.Contracts;
using OctaGrid.Protocol.Agents;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Client.Services;

internal sealed class AgentCommandsService : IAgentCommandsService
{
    public Task DeleteFileAsync(Guid deviceId, string fileName, CancellationToken cancellationToken) =>
        Task.CompletedTask;

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

    public Task<ConnectionTestResult> TestConnectionAsync(string protocol, string connectionString, string? apiKey, CancellationToken cancellationToken) =>
        Task.FromResult(new ConnectionTestResult(true, null));

    public Task TriggerInitialStateAsync(Guid printerId, CancellationToken cancellationToken) =>
                            Task.CompletedTask;

    public Task TriggerInitialStateAsync(IEnumerable<Guid> printers, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task UploadFileAsync(Guid deviceId, Stream stream, string fileName, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
