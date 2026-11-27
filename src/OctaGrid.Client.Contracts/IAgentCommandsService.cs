using OctaGrid.Extensions.Models;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Contracts;

public interface IAgentCommandsService
{
    /// <summary>
    /// The connectors an agent offers to add printers with, including their settings and capabilities. Each agent has
    /// its own set, because extensions are installed per agent.
    /// </summary>
    /// <exception cref="OctaGrid.Extensions.Models.Exceptions.AgentUnavailableException">The agent cannot be reached.</exception>
    Task<IReadOnlyList<ConnectorDescriptor>> GetConnectorsAsync(Guid agentId, CancellationToken cancellationToken);

    Task DeleteFileAsync(Guid printerId, string fileName, CancellationToken cancellationToken);

    /// <summary>
    /// What a printer wrote to its console, oldest first. Pass the sequence of the last line already shown as
    /// <paramref name="afterSequence"/> to get only what came after it, or null for the newest lines. Only printers whose
    /// connector declares <see cref="ConnectorCapabilities.Console"/> have any.
    /// </summary>
    Task<ConsolePage> GetPrinterConsoleAsync(Guid printerId, long? afterSequence, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// What happened to a printer, oldest first. Pass the sequence of the last event already shown as
    /// <paramref name="afterSequence"/> to get only what came after it, or null for the newest events. The page says
    /// when events were lost because the agent keeps a bounded history.
    /// </summary>
    Task<PrinterEventPage> GetPrinterEventsAsync(Guid printerId, long? afterSequence, int limit, CancellationToken cancellationToken);

    Task<IEnumerable<PrintMetadata>> GetPrinterFilesAsync(Guid printerId, CancellationToken cancellationToken);

    /// <summary>
    /// The full metadata of files <see cref="GetPrinterFilesAsync"/> returned with
    /// <see cref="PrintMetadata.DetailsPending"/> set, in any order. Ask for the files on screen, a few at a time.
    /// </summary>
    Task<IReadOnlyList<PrintMetadata>> GetPrinterFileDetailsAsync(Guid printerId, IReadOnlyList<string> fileNames, CancellationToken cancellationToken);

    Task<PrintThumbnail?> GetThumbnailAsync(Guid printerId, string fileName, CancellationToken cancellationToken);

    Task PausePrintJobAsync(Guid printerId, CancellationToken cancellationToken);

    Task ResumePrintJobAsync(Guid printerId, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a command the user typed (one line, see <see cref="ConsoleCommandRules"/>) to a printer whose connector
    /// declares <see cref="ConnectorCapabilities.ConsoleCommands"/>. It can change or stop what the printer is doing, so
    /// clients call it only after the user turned on Expert mode. What the printer answers appears in its console.
    /// </summary>
    /// <exception cref="OctaGrid.Extensions.Models.Exceptions.ValidationException">The command is not acceptable, or the printer cannot take commands.</exception>
    Task SendConsoleCommandAsync(Guid printerId, string command, CancellationToken cancellationToken);

    /// <summary>
    /// What starting a file involves: the plates to choose from and the connector's start options. Null when the
    /// printer's connector offers no choices; the file then starts with <see cref="StartPrintJobAsync"/> as it is.
    /// </summary>
    Task<PrintStartPlan?> GetPrintStartPlanAsync(Guid printerId, string fileName, CancellationToken cancellationToken);

    /// <summary>Starts a file, with what the user chose from its start plan, or null for the connector's defaults.</summary>
    Task StartPrintJobAsync(Guid printerId, string fileName, PrintStartChoices? choices, CancellationToken cancellationToken);

    Task StopPrintJobAsync(Guid printerId, CancellationToken cancellationToken);

    /// <param name="workspaceId">The workspace a new printer is created in.</param>
    /// <param name="settings">The settings entered for the printer, including secrets, by the keys the connector declared.</param>
    /// <param name="printerId">
    /// The saved printer being edited, or null for a new one. Saved secrets cannot be read by a client, so for a
    /// saved printer the secrets the user left empty are filled in from what is saved.
    /// </param>
    /// <param name="agentId">
    /// The agent that will run a new printer, and so tests it; null for the agent the printer would be created
    /// with (the workspace's only agent). A saved printer that has an agent is always tested by its own agent.
    /// </param>
    Task<ConnectionTestResult> TestConnectionAsync(
        Guid workspaceId,
        string connectorId,
        IReadOnlyDictionary<string, string> settings,
        Guid? printerId,
        Guid? agentId,
        CancellationToken cancellationToken);

    Task UploadFileAsync(Guid printerId, Stream stream, string fileName, CancellationToken cancellationToken);
}
