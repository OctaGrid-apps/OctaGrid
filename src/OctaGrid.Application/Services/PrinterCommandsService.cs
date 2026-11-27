using OctaGrid.Application.Agents;
using OctaGrid.Application.Authorization;
using OctaGrid.Application.Secrets;
using OctaGrid.Application.Validation;
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
    private static readonly FileDetailsRequestValidator FileDetailsValidator = new();
    private static readonly PrintStartChoicesValidator StartChoicesValidator = new();

    private readonly WorkspaceAccess _access;
    private readonly IAgentLink _link;
    private readonly PrinterRouting _routing;
    private readonly PrinterSecretStore _secrets;

    public PrinterCommandsService(WorkspaceAccess access, PrinterRouting routing, PrinterSecretStore secrets, IAgentLink link)
    {
        _access = access;
        _routing = routing;
        _secrets = secrets;
        _link = link;
    }

    /// <summary>
    /// The connectors an agent offers, with their settings and capabilities. Each agent has its own set, because
    /// extensions are installed per agent.
    /// </summary>
    /// <exception cref="NotFoundException">There is no such agent.</exception>
    /// <exception cref="AgentUnavailableException">The agent cannot be reached.</exception>
    public async Task<IReadOnlyList<ConnectorDescriptor>> GetConnectors(Guid userId, Guid agentId, CancellationToken cancellationToken)
    {
        await _access.RequireForAgent(userId, agentId, Permission.View, cancellationToken);
        return (await _link.SendAsync(agentId, new GetConnectorsRequest(), cancellationToken)).Connectors;
    }

    public async Task DeleteFile(Guid userId, Guid printerId, string fileName, CancellationToken cancellationToken) =>
        await _link.SendAsync(await AgentOf(userId, printerId, Permission.ManageFiles, cancellationToken), new DeletePrinterFileRequest(printerId, fileName), cancellationToken);

    public async Task<ConsolePage> GetConsole(Guid userId, Guid printerId, long? afterSequence, int limit, CancellationToken cancellationToken) =>
        (await _link.SendAsync(await AgentOf(userId, printerId, Permission.View, cancellationToken), new GetPrinterConsoleRequest(printerId, afterSequence, limit), cancellationToken)).Page;

    public async Task<PrinterEventPage> GetEvents(Guid userId, Guid printerId, long? afterSequence, int limit, CancellationToken cancellationToken) =>
        (await _link.SendAsync(await AgentOf(userId, printerId, Permission.View, cancellationToken), new GetPrinterEventsRequest(printerId, afterSequence, limit), cancellationToken)).Page;

    public async Task<IReadOnlyList<PrintMetadata>> GetFiles(Guid userId, Guid printerId, CancellationToken cancellationToken) =>
        (await _link.SendAsync(await AgentOf(userId, printerId, Permission.View, cancellationToken), new ListPrinterFilesRequest(printerId), cancellationToken)).Files;

    /// <summary>The full metadata of files the listing returned with <see cref="PrintMetadata.DetailsPending"/> set.</summary>
    public async Task<IReadOnlyList<PrintMetadata>> GetFileDetails(
        Guid userId, Guid printerId, IReadOnlyList<string> fileNames, CancellationToken cancellationToken)
    {
        var agentId = await AgentOf(userId, printerId, Permission.View, cancellationToken);
        FileDetailsValidator.ValidateOrThrow(fileNames);
        return (await _link.SendAsync(agentId, new GetPrinterFileDetailsRequest(printerId, fileNames), cancellationToken)).Files;
    }

    public async Task<PrintThumbnail?> GetThumbnail(Guid userId, Guid printerId, string fileName, CancellationToken cancellationToken) =>
        (await _link.SendAsync(await AgentOf(userId, printerId, Permission.View, cancellationToken), new GetPrintThumbnailRequest(printerId, fileName), cancellationToken)).Thumbnail;

    public async Task PausePrint(Guid userId, Guid printerId, CancellationToken cancellationToken) =>
        await _link.SendAsync(await AgentOf(userId, printerId, Permission.ControlPrint, cancellationToken), new PausePrintRequest(printerId), cancellationToken);

    public async Task ResumePrint(Guid userId, Guid printerId, CancellationToken cancellationToken) =>
        await _link.SendAsync(await AgentOf(userId, printerId, Permission.ControlPrint, cancellationToken), new ResumePrintRequest(printerId), cancellationToken);

    public async Task SendConsoleCommand(Guid userId, Guid printerId, string command, CancellationToken cancellationToken) =>
        await _link.SendAsync(await AgentOf(userId, printerId, Permission.SendCommands, cancellationToken), new SendConsoleCommandRequest(printerId, command), cancellationToken);

    /// <summary>
    /// What starting a file involves: its plates and the start options, which the user chooses from before the print
    /// starts. Null when the printer's connector offers no choices.
    /// </summary>
    public async Task<PrintStartPlan?> GetPrintStartPlan(Guid userId, Guid printerId, string fileName, CancellationToken cancellationToken) =>
        (await _link.SendAsync(await AgentOf(userId, printerId, Permission.ControlPrint, cancellationToken), new GetPrintStartPlanRequest(printerId, fileName), cancellationToken)).Plan;

    /// <summary>Starts printing a file, with what the user chose from its start plan, or null for the defaults.</summary>
    public async Task StartPrint(Guid userId, Guid printerId, string fileName, PrintStartChoices? choices, CancellationToken cancellationToken)
    {
        var agentId = await AgentOf(userId, printerId, Permission.ControlPrint, cancellationToken);
        if (choices is not null)
        {
            StartChoicesValidator.ValidateOrThrow(choices);
        }

        await _link.SendAsync(agentId, new StartPrintRequest(printerId, fileName) { Choices = choices }, cancellationToken);
    }

    public async Task StopPrint(Guid userId, Guid printerId, CancellationToken cancellationToken) =>
        await _link.SendAsync(await AgentOf(userId, printerId, Permission.ControlPrint, cancellationToken), new StopPrintRequest(printerId), cancellationToken);

    /// <summary>
    /// Tests connection settings. For a saved printer (<paramref name="printerId"/>) the secrets the user left
    /// untouched are filled in from the saved ones, and its own agent runs the test. A new printer, or a saved one
    /// without an agent, is tested by the agent it would be created with: <paramref name="agentId"/>, or else the
    /// only agent of <paramref name="workspaceId"/>. The user must be allowed to use that agent.
    /// </summary>
    public async Task<ConnectionTestResult> TestConnection(
        Guid userId,
        Guid workspaceId,
        string connectorId,
        IReadOnlyDictionary<string, string> settings,
        Guid? printerId,
        Guid? agentId,
        CancellationToken cancellationToken)
    {
        Guid? target = null;
        if (printerId is { } id)
        {
            // Authorizes the user before the saved secrets are read.
            target = await _routing.AssignedAgentOf(userId, id, Permission.ManagePrinters, cancellationToken);
            settings = (await _secrets.ReadSavedAsync(id, cancellationToken)).With(settings).Values;
        }

        if (target is null)
        {
            if (agentId is null)
            {
                await _access.Require(userId, workspaceId, Permission.ManagePrinters, cancellationToken);
            }

            target = agentId
                ?? (await _routing.AgentForNewPrinter(workspaceId, requested: null, cancellationToken))?.Id
                ?? throw new ValidationException(nameof(agentId), "Choose the agent that tests the connection.");
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
        _routing.AgentOf(userId, printerId, permission, cancellationToken);
}
