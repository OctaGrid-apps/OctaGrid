using OctaGrid.Agent.Core;
using OctaGrid.Client.Contracts;
using OctaGrid.Protocol.Agents;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Client.Local;

internal sealed class LocalAgentCommands(AgentWorker agentWorker) : IAgentCommandsService, ICommandService
{
    public Task DeleteFileAsync(Guid deviceId, string fileName, CancellationToken cancellationToken)
        => agentWorker.DeleteFileAsync(deviceId, fileName, cancellationToken);

    public Task<IEnumerable<PrintMetadata>> GetPrinterFilesAsync(Guid deviceId, CancellationToken cancellationToken) =>
        agentWorker.GetPrinterFilesAsync(deviceId, cancellationToken);

    public Task<PrintThumbnail?> GetThumbnailAsync(Guid printerId, string fileName, CancellationToken cancellationToken) =>
        agentWorker.GetThumbnailAsync(printerId, fileName, cancellationToken);

    public Task PausePrintJobAsync(Guid deviceId, CancellationToken cancellationToken)
        => agentWorker.PausePrintJobAsync(deviceId, cancellationToken);

    public Task ResumePrintJobAsync(Guid deviceId, CancellationToken cancellationToken)
        => agentWorker.ResumePrintJobAsync(deviceId, cancellationToken);

    public Task StartPrintJobAsync(Guid deviceId, string fileName, CancellationToken cancellationToken)
        => agentWorker.StartPrintJobAsync(deviceId, fileName, cancellationToken);

    public Task StopPrintJobAsync(Guid deviceId, CancellationToken cancellationToken)
        => agentWorker.StopPrintJobAsync(deviceId, cancellationToken);

    public Task<ConnectionTestResult> TestConnectionAsync(string protocol, string connectionString, string? apiKey, CancellationToken cancellationToken) =>
        agentWorker.TestConnectionAsync(protocol, connectionString, apiKey, cancellationToken);

    public Task TriggerInitialStateAsync(Guid printerId, CancellationToken cancellationToken) =>
                                        agentWorker.TriggerInitialStateAsync(printerId, cancellationToken);

    public Task TriggerInitialStateAsync(IEnumerable<Guid> printers, CancellationToken cancellationToken) =>
        agentWorker.TriggerInitialStateAsync(printers, cancellationToken);

    public Task UploadFileAsync(Guid deviceId, Stream stream, string fileName, CancellationToken cancellationToken)
        => agentWorker.UploadFileAsync(deviceId, stream, fileName, cancellationToken);
}
