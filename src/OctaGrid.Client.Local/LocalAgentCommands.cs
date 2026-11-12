using Microsoft.Extensions.DependencyInjection;
using OctaGrid.Agent.Core;
using OctaGrid.Application.Services;
using OctaGrid.Client.Contracts;
using OctaGrid.Extensions.Models;
using OctaGrid.Extensions.Models.Exceptions;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Local;

internal sealed class LocalAgentCommands(AgentWorker agentWorker, IServiceScopeFactory scopes, ICurrentUserAccessor currentUser)
    : IAgentCommandsService, ICommandService
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

    public async Task<ConnectionTestResult> TestConnectionAsync(string connectorId, IReadOnlyDictionary<string, string> settings, Guid? printerId, CancellationToken cancellationToken)
    {
        if (printerId is { } id)
        {
            var userId = currentUser.GetCurrentUserId()
                ?? throw new AuthenticationException("No user is signed in.");

            // The service is scoped (it uses the DbContext), so it gets its own scope for this call.
            await using var scope = scopes.CreateAsyncScope();
            settings = await scope.ServiceProvider.GetRequiredService<PrintersService>()
                .AddSavedSecrets(userId, id, settings, cancellationToken);
        }

        return await agentWorker.TestConnectionAsync(connectorId, settings, cancellationToken);
    }

    public Task TriggerInitialStateAsync(Guid printerId, CancellationToken cancellationToken) =>
                                        agentWorker.TriggerInitialStateAsync(printerId, cancellationToken);

    public Task TriggerInitialStateAsync(IEnumerable<Guid> printers, CancellationToken cancellationToken) =>
        agentWorker.TriggerInitialStateAsync(printers, cancellationToken);

    public Task UploadFileAsync(Guid deviceId, Stream stream, string fileName, CancellationToken cancellationToken)
        => agentWorker.UploadFileAsync(deviceId, stream, fileName, cancellationToken);
}
