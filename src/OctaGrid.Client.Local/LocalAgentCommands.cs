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
/// What the UI asks of printers, sent as messages over the <see cref="IAgentLink"/> to the agent each printer is
/// assigned to, the way a server routes them. Nothing here touches the agent directly.
/// </summary>
internal sealed class LocalAgentCommands : IAgentCommandsService, ICommandService
{
    private readonly LocalConnectorCache _connectors;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAgentLink _link;
    private readonly LocalAgent _localAgent;
    private readonly IServiceScopeFactory _scopes;

    public LocalAgentCommands(
        IAgentLink link,
        LocalAgent localAgent,
        LocalConnectorCache connectors,
        IServiceScopeFactory scopes,
        ICurrentUserAccessor currentUser)
    {
        _link = link;
        _localAgent = localAgent;
        _connectors = connectors;
        _scopes = scopes;
        _currentUser = currentUser;
    }

    public IReadOnlyList<ConnectorDescriptor> Connectors => _connectors.Connectors;

    public async Task DeleteFileAsync(Guid deviceId, string fileName, CancellationToken cancellationToken) =>
        await _link.SendAsync(await AgentOfAsync(deviceId, cancellationToken), new DeletePrinterFileRequest(deviceId, fileName), cancellationToken);

    public async Task<ConsolePage> GetPrinterConsoleAsync(Guid printerId, long? afterSequence, int limit, CancellationToken cancellationToken) =>
        (await _link.SendAsync(await AgentOfAsync(printerId, cancellationToken), new GetPrinterConsoleRequest(printerId, afterSequence, limit), cancellationToken)).Page;

    public async Task<PrinterEventPage> GetPrinterEventsAsync(Guid printerId, long? afterSequence, int limit, CancellationToken cancellationToken) =>
        (await _link.SendAsync(await AgentOfAsync(printerId, cancellationToken), new GetPrinterEventsRequest(printerId, afterSequence, limit), cancellationToken)).Page;

    public async Task<IEnumerable<PrintMetadata>> GetPrinterFilesAsync(Guid deviceId, CancellationToken cancellationToken) =>
        (await _link.SendAsync(await AgentOfAsync(deviceId, cancellationToken), new ListPrinterFilesRequest(deviceId), cancellationToken)).Files;

    public async Task<PrintThumbnail?> GetThumbnailAsync(Guid printerId, string fileName, CancellationToken cancellationToken) =>
        (await _link.SendAsync(await AgentOfAsync(printerId, cancellationToken), new GetPrintThumbnailRequest(printerId, fileName), cancellationToken)).Thumbnail;

    public async Task PausePrintJobAsync(Guid deviceId, CancellationToken cancellationToken) =>
        await _link.SendAsync(await AgentOfAsync(deviceId, cancellationToken), new PausePrintRequest(deviceId), cancellationToken);

    public async Task ResumePrintJobAsync(Guid deviceId, CancellationToken cancellationToken) =>
        await _link.SendAsync(await AgentOfAsync(deviceId, cancellationToken), new ResumePrintRequest(deviceId), cancellationToken);

    public async Task SendConsoleCommandAsync(Guid printerId, string command, CancellationToken cancellationToken) =>
        await _link.SendAsync(await AgentOfAsync(printerId, cancellationToken), new SendConsoleCommandRequest(printerId, command), cancellationToken);

    public async Task StartPrintJobAsync(Guid deviceId, string fileName, CancellationToken cancellationToken) =>
        await _link.SendAsync(await AgentOfAsync(deviceId, cancellationToken), new StartPrintRequest(deviceId, fileName), cancellationToken);

    public async Task StopPrintJobAsync(Guid deviceId, CancellationToken cancellationToken) =>
        await _link.SendAsync(await AgentOfAsync(deviceId, cancellationToken), new StopPrintRequest(deviceId), cancellationToken);

    public async Task<ConnectionTestResult> TestConnectionAsync(
        string connectorId, IReadOnlyDictionary<string, string> settings, Guid? printerId, CancellationToken cancellationToken)
    {
        // A new printer is tested by the agent it will get, which in Local mode is the local one.
        var agentId = _localAgent.Id;
        if (printerId is { } id)
        {
            // The service is scoped (it uses the DbContext), so it gets its own scope for this call.
            await using var scope = _scopes.CreateAsyncScope();
            var printers = scope.ServiceProvider.GetRequiredService<PrintersService>();
            var userId = CurrentUserId();
            settings = await printers.AddSavedSecrets(userId, id, settings, cancellationToken);

            // The saved secrets go only to the printer's own agent; a printer without one is tested locally.
            var agents = await printers.GetAgentIds(userId, [id], cancellationToken);
            agentId = agents.GetValueOrDefault(id, agentId);
        }

        return (await _link.SendAsync(agentId, new TestConnectionRequest(connectorId, settings), cancellationToken)).Result;
    }

    public Task TriggerInitialStateAsync(Guid printerId, CancellationToken cancellationToken) =>
        TriggerInitialStateAsync([printerId], cancellationToken);

    public async Task TriggerInitialStateAsync(IEnumerable<Guid> printers, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<Guid, Guid> agents;
        await using (var scope = _scopes.CreateAsyncScope())
        {
            agents = await scope.ServiceProvider.GetRequiredService<PrintersService>()
                .GetAgentIds(CurrentUserId(), printers.ToList(), cancellationToken);
        }

        // A printer without an agent has no state to report.
        foreach (var group in agents.GroupBy(a => a.Value, a => a.Key))
        {
            await _link.SendAsync(group.Key, new RequestPrinterStateRequest(group.ToList()), cancellationToken);
        }
    }

    public async Task UploadFileAsync(Guid deviceId, Stream stream, string fileName, CancellationToken cancellationToken)
    {
        var agentId = await AgentOfAsync(deviceId, cancellationToken);
        var uploadId = await _link.UploadAsync(agentId, stream, cancellationToken);
        try
        {
            await _link.SendAsync(agentId, new StorePrinterFileRequest(deviceId, uploadId, fileName), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Cancelled before the agent took the upload: do not leave it behind.
            await AgentLinkUploads.AbortQuietlyAsync(_link, agentId, uploadId);
            throw;
        }
    }

    /// <summary>The agent a printer is assigned to, checked against what the current user may reach.</summary>
    private async Task<Guid> AgentOfAsync(Guid printerId, CancellationToken cancellationToken)
    {
        await using var scope = _scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PrintersService>()
            .GetAgentId(CurrentUserId(), printerId, cancellationToken);
    }

    private Guid CurrentUserId() =>
        _currentUser.GetCurrentUserId() ?? throw new AuthenticationException("No user is signed in.");
}
