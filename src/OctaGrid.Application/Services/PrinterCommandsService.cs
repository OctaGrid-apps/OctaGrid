using OctaGrid.Application.Agents;
using OctaGrid.Application.Authorization;
using OctaGrid.Extensions.Models;
using OctaGrid.Extensions.Models.Exceptions;
using OctaGrid.Protocol.AgentLink.Requests;
using OctaGrid.Protocol.Messaging;
using OctaGrid.Protocol.Permissions;

namespace OctaGrid.Application.Services;

/// <summary>
/// What a user asks of a printer. Every call checks that the user may use the printer, then goes over the
/// <see cref="IAgentLink"/> to the agent the printer is assigned to and to no other.
/// </summary>
public sealed class PrinterCommandsService
{
    private readonly WorkspaceAccess _access;
    private readonly IAgentLink _link;
    private readonly PrintersService _printers;

    public PrinterCommandsService(WorkspaceAccess access, PrintersService printers, IAgentLink link)
    {
        _access = access;
        _printers = printers;
        _link = link;
    }

    public async Task DeleteFile(Guid userId, Guid printerId, string fileName, CancellationToken cancellationToken) =>
        await _link.SendAsync(await AgentOf(userId, printerId, Permission.ManageFiles, cancellationToken), new DeletePrinterFileRequest(printerId, fileName), cancellationToken);

    public async Task<ConsolePage> GetConsole(Guid userId, Guid printerId, long? afterSequence, int limit, CancellationToken cancellationToken) =>
        (await _link.SendAsync(await AgentOf(userId, printerId, Permission.View, cancellationToken), new GetPrinterConsoleRequest(printerId, afterSequence, limit), cancellationToken)).Page;

    public async Task<PrinterEventPage> GetEvents(Guid userId, Guid printerId, long? afterSequence, int limit, CancellationToken cancellationToken) =>
        (await _link.SendAsync(await AgentOf(userId, printerId, Permission.View, cancellationToken), new GetPrinterEventsRequest(printerId, afterSequence, limit), cancellationToken)).Page;

    public async Task<IReadOnlyList<PrintMetadata>> GetFiles(Guid userId, Guid printerId, CancellationToken cancellationToken) =>
        (await _link.SendAsync(await AgentOf(userId, printerId, Permission.View, cancellationToken), new ListPrinterFilesRequest(printerId), cancellationToken)).Files;

    public async Task<PrintThumbnail?> GetThumbnail(Guid userId, Guid printerId, string fileName, CancellationToken cancellationToken) =>
        (await _link.SendAsync(await AgentOf(userId, printerId, Permission.View, cancellationToken), new GetPrintThumbnailRequest(printerId, fileName), cancellationToken)).Thumbnail;

    public async Task PausePrint(Guid userId, Guid printerId, CancellationToken cancellationToken) =>
        await _link.SendAsync(await AgentOf(userId, printerId, Permission.ControlPrint, cancellationToken), new PausePrintRequest(printerId), cancellationToken);

    public async Task ResumePrint(Guid userId, Guid printerId, CancellationToken cancellationToken) =>
        await _link.SendAsync(await AgentOf(userId, printerId, Permission.ControlPrint, cancellationToken), new ResumePrintRequest(printerId), cancellationToken);

    public async Task SendConsoleCommand(Guid userId, Guid printerId, string command, CancellationToken cancellationToken) =>
        await _link.SendAsync(await AgentOf(userId, printerId, Permission.SendCommands, cancellationToken), new SendConsoleCommandRequest(printerId, command), cancellationToken);

    public async Task StartPrint(Guid userId, Guid printerId, string fileName, CancellationToken cancellationToken) =>
        await _link.SendAsync(await AgentOf(userId, printerId, Permission.ControlPrint, cancellationToken), new StartPrintRequest(printerId, fileName), cancellationToken);

    public async Task StopPrint(Guid userId, Guid printerId, CancellationToken cancellationToken) =>
        await _link.SendAsync(await AgentOf(userId, printerId, Permission.ControlPrint, cancellationToken), new StopPrintRequest(printerId), cancellationToken);

    /// <summary>
    /// Tests connection settings. For a saved printer (<paramref name="printerId"/>) the secrets the user left
    /// untouched are filled in from the saved ones, and its own agent runs the test. A new printer, or a saved one
    /// without an agent, is tested by <paramref name="agentId"/>, which the user must be allowed to use.
    /// </summary>
    public async Task<ConnectionTestResult> TestConnection(
        Guid userId,
        string connectorId,
        IReadOnlyDictionary<string, string> settings,
        Guid? printerId,
        Guid? agentId,
        CancellationToken cancellationToken)
    {
        Guid? target = null;
        if (printerId is { } id)
        {
            settings = await _printers.AddSavedSecrets(userId, id, settings, cancellationToken);
            var agents = await _printers.GetAgentIds(userId, [id], Permission.ManagePrinters, cancellationToken);
            target = agents.TryGetValue(id, out var own) ? own : null;
        }

        if (target is null)
        {
            target = agentId ?? throw new ValidationException(nameof(agentId), "Choose the agent that tests the connection.");
            await _access.RequireForAgent(userId, target.Value, Permission.ManagePrinters, cancellationToken);
        }

        return (await _link.SendAsync(target.Value, new TestConnectionRequest(connectorId, settings), cancellationToken)).Result;
    }

    public async Task UploadFile(Guid userId, Guid printerId, Stream content, string fileName, CancellationToken cancellationToken)
    {
        var agentId = await AgentOf(userId, printerId, Permission.ManageFiles, cancellationToken);
        var uploadId = await _link.UploadAsync(agentId, content, cancellationToken);
        try
        {
            await _link.SendAsync(agentId, new StorePrinterFileRequest(printerId, uploadId, fileName), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Cancelled before the agent took the upload: do not leave it behind.
            await AgentLinkUploads.AbortQuietlyAsync(_link, agentId, uploadId);
            throw;
        }
    }

    // Also the authorization: it fails unless the user has the permission in the printer's workspace.
    private Task<Guid> AgentOf(Guid userId, Guid printerId, Permission permission, CancellationToken cancellationToken) =>
        _printers.GetAgentId(userId, printerId, permission, cancellationToken);
}
