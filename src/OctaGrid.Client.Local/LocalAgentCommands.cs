using OctaGrid.Agent.Core;
using OctaGrid.Client.Contracts;
using OctaGrid.Protocol.Agents;
using OctaGrid.Protocol.Extensions;
using OctaGrid.Protocol.Messaging;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Client.Local;

internal sealed class LocalAgentCommands(AgentWorker agentWorker) : IAgentCommandsService, ICommandService
{
    public Task DeleteFileAsync(Guid deviceId, string fileName, CancellationToken cancellationToken)
        => agentWorker.DeleteFileAsync(deviceId, fileName, cancellationToken);

    public Task<ConsolePage> GetPrinterConsoleAsync(Guid printerId, long? afterSequence, int limit, CancellationToken cancellationToken) =>
        Task.FromResult(agentWorker.GetPrinterConsole(printerId, afterSequence, limit));

    public Task<PrinterEventPage> GetPrinterEventsAsync(Guid printerId, long? afterSequence, int limit, CancellationToken cancellationToken) =>
        Task.FromResult(agentWorker.GetPrinterEvents(printerId, afterSequence, limit));

    public Task<IEnumerable<PrintMetadata>> GetPrinterFilesAsync(Guid deviceId, CancellationToken cancellationToken) =>
        agentWorker.GetPrinterFilesAsync(deviceId, cancellationToken);

    public Task<PrintThumbnail?> GetThumbnailAsync(Guid printerId, string fileName, CancellationToken cancellationToken) =>
        agentWorker.GetThumbnailAsync(printerId, fileName, cancellationToken);

    public Task PausePrintJobAsync(Guid deviceId, CancellationToken cancellationToken)
        => agentWorker.PausePrintJobAsync(deviceId, cancellationToken);

    public Task ResumePrintJobAsync(Guid deviceId, CancellationToken cancellationToken)
        => agentWorker.ResumePrintJobAsync(deviceId, cancellationToken);

    public Task SendConsoleCommandAsync(Guid printerId, string command, CancellationToken cancellationToken)
        => agentWorker.SendConsoleCommandAsync(printerId, command, cancellationToken);

    public Task StartPrintJobAsync(Guid deviceId, string fileName, CancellationToken cancellationToken)
        => agentWorker.StartPrintJobAsync(deviceId, fileName, cancellationToken);

    public Task StopPrintJobAsync(Guid deviceId, CancellationToken cancellationToken)
        => agentWorker.StopPrintJobAsync(deviceId, cancellationToken);

    public IReadOnlyList<ConnectorDescriptor> Connectors => agentWorker.ConnectorDescriptors;

    public Task<ConnectionTestResult> TestConnectionAsync(string connectorId, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken) =>
        agentWorker.TestConnectionAsync(connectorId, settings, cancellationToken);

    public Task TriggerInitialStateAsync(Guid printerId, CancellationToken cancellationToken) =>
                                        agentWorker.TriggerInitialStateAsync(printerId, cancellationToken);

    public Task TriggerInitialStateAsync(IEnumerable<Guid> printers, CancellationToken cancellationToken) =>
        agentWorker.TriggerInitialStateAsync(printers, cancellationToken);

    public Task UploadFileAsync(Guid deviceId, Stream stream, string fileName, CancellationToken cancellationToken)
        => agentWorker.UploadFileAsync(deviceId, stream, fileName, cancellationToken);
}
