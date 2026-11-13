using Microsoft.Extensions.DependencyInjection;
using OctaGrid.Agent.Core;
using OctaGrid.Application.Agents;
using OctaGrid.Application.Services;
using OctaGrid.Client.Contracts;
using OctaGrid.Extensions.Models;
using OctaGrid.Extensions.Models.Exceptions;
using OctaGrid.Protocol.AgentLink.Requests;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Local;

/// <summary>
/// What the UI asks of printers, sent to the local agent as messages over the <see cref="IAgentLink"/>.
/// Nothing here touches the agent directly, so the same calls work against a remote agent.
/// </summary>
internal sealed class LocalAgentCommands : IAgentCommandsService, ICommandService
{
    private readonly LocalConnectorCache _connectors;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAgentLink _link;
    private readonly IServiceScopeFactory _scopes;

    public LocalAgentCommands(
        IAgentLink link, LocalConnectorCache connectors, IServiceScopeFactory scopes, ICurrentUserAccessor currentUser)
    {
        _link = link;
        _connectors = connectors;
        _scopes = scopes;
        _currentUser = currentUser;
    }

    public IReadOnlyList<ConnectorDescriptor> Connectors => _connectors.Connectors;

    public async Task DeleteFileAsync(Guid deviceId, string fileName, CancellationToken cancellationToken) =>
        await _link.SendAsync(LocalAgent.Id, new DeletePrinterFileRequest(deviceId, fileName), cancellationToken);

    public async Task<ConsolePage> GetPrinterConsoleAsync(Guid printerId, long? afterSequence, int limit, CancellationToken cancellationToken) =>
        (await _link.SendAsync(LocalAgent.Id, new GetPrinterConsoleRequest(printerId, afterSequence, limit), cancellationToken)).Page;

    public async Task<PrinterEventPage> GetPrinterEventsAsync(Guid printerId, long? afterSequence, int limit, CancellationToken cancellationToken) =>
        (await _link.SendAsync(LocalAgent.Id, new GetPrinterEventsRequest(printerId, afterSequence, limit), cancellationToken)).Page;

    public async Task<IEnumerable<PrintMetadata>> GetPrinterFilesAsync(Guid deviceId, CancellationToken cancellationToken) =>
        (await _link.SendAsync(LocalAgent.Id, new ListPrinterFilesRequest(deviceId), cancellationToken)).Files;

    public async Task<PrintThumbnail?> GetThumbnailAsync(Guid printerId, string fileName, CancellationToken cancellationToken) =>
        (await _link.SendAsync(LocalAgent.Id, new GetPrintThumbnailRequest(printerId, fileName), cancellationToken)).Thumbnail;

    public async Task PausePrintJobAsync(Guid deviceId, CancellationToken cancellationToken) =>
        await _link.SendAsync(LocalAgent.Id, new PausePrintRequest(deviceId), cancellationToken);

    public async Task ResumePrintJobAsync(Guid deviceId, CancellationToken cancellationToken) =>
        await _link.SendAsync(LocalAgent.Id, new ResumePrintRequest(deviceId), cancellationToken);

    public async Task SendConsoleCommandAsync(Guid printerId, string command, CancellationToken cancellationToken) =>
        await _link.SendAsync(LocalAgent.Id, new SendConsoleCommandRequest(printerId, command), cancellationToken);

    public async Task StartPrintJobAsync(Guid deviceId, string fileName, CancellationToken cancellationToken) =>
        await _link.SendAsync(LocalAgent.Id, new StartPrintRequest(deviceId, fileName), cancellationToken);

    public async Task StopPrintJobAsync(Guid deviceId, CancellationToken cancellationToken) =>
        await _link.SendAsync(LocalAgent.Id, new StopPrintRequest(deviceId), cancellationToken);

    public async Task<ConnectionTestResult> TestConnectionAsync(
        string connectorId, IReadOnlyDictionary<string, string> settings, Guid? printerId, CancellationToken cancellationToken)
    {
        if (printerId is { } id)
        {
            var userId = _currentUser.GetCurrentUserId()
                ?? throw new AuthenticationException("No user is signed in.");

            // The service is scoped (it uses the DbContext), so it gets its own scope for this call.
            await using var scope = _scopes.CreateAsyncScope();
            settings = await scope.ServiceProvider.GetRequiredService<PrintersService>()
                .AddSavedSecrets(userId, id, settings, cancellationToken);
        }

        return (await _link.SendAsync(LocalAgent.Id, new TestConnectionRequest(connectorId, settings), cancellationToken)).Result;
    }

    public Task TriggerInitialStateAsync(Guid printerId, CancellationToken cancellationToken) =>
        TriggerInitialStateAsync([printerId], cancellationToken);

    public async Task TriggerInitialStateAsync(IEnumerable<Guid> printers, CancellationToken cancellationToken) =>
        await _link.SendAsync(LocalAgent.Id, new RequestPrinterStateRequest(printers.ToList()), cancellationToken);

    public async Task UploadFileAsync(Guid deviceId, Stream stream, string fileName, CancellationToken cancellationToken)
    {
        var uploadId = await _link.UploadAsync(LocalAgent.Id, stream, cancellationToken);
        try
        {
            await _link.SendAsync(LocalAgent.Id, new StorePrinterFileRequest(deviceId, uploadId, fileName), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Cancelled before the agent took the upload: do not leave it behind.
            await AgentLinkUploads.AbortQuietlyAsync(_link, LocalAgent.Id, uploadId);
            throw;
        }
    }
}
