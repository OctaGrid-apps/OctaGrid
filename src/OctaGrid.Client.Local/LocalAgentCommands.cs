using OctaGrid.Application.Services;
using OctaGrid.Client.Contracts;
using OctaGrid.Extensions.Models;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Local;

/// <summary>
/// What the UI asks of printers, handed to <see cref="PrinterCommandsService"/> as the local user. The service
/// authorizes the call and sends it to the printer's agent, the way a server does.
/// </summary>
internal sealed class LocalAgentCommands : IAgentCommandsService
{
    private readonly LocalCalls _calls;

    public LocalAgentCommands(LocalCalls calls)
    {
        _calls = calls;
    }

    public Task<IReadOnlyList<ConnectorDescriptor>> GetConnectorsAsync(Guid agentId, CancellationToken cancellationToken) =>
        _calls.Run<PrinterCommandsService, IReadOnlyList<ConnectorDescriptor>>((printers, userId) => printers.GetConnectors(userId, agentId, cancellationToken), cancellationToken);

    public Task DeleteFileAsync(Guid printerId, string fileName, CancellationToken cancellationToken) =>
        _calls.Run<PrinterCommandsService>((printers, userId) => printers.DeleteFile(userId, printerId, fileName, cancellationToken), cancellationToken);

    public Task<ConsolePage> GetPrinterConsoleAsync(Guid printerId, long? afterSequence, int limit, CancellationToken cancellationToken) =>
        _calls.Run<PrinterCommandsService, ConsolePage>((printers, userId) => printers.GetConsole(userId, printerId, afterSequence, limit, cancellationToken), cancellationToken);

    public Task<PrinterEventPage> GetPrinterEventsAsync(Guid printerId, long? afterSequence, int limit, CancellationToken cancellationToken) =>
        _calls.Run<PrinterCommandsService, PrinterEventPage>((printers, userId) => printers.GetEvents(userId, printerId, afterSequence, limit, cancellationToken), cancellationToken);

    public Task<IEnumerable<PrintMetadata>> GetPrinterFilesAsync(Guid printerId, CancellationToken cancellationToken) =>
        _calls.Run<PrinterCommandsService, IEnumerable<PrintMetadata>>(async (printers, userId) => await printers.GetFiles(userId, printerId, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<PrintMetadata>> GetPrinterFileDetailsAsync(Guid printerId, IReadOnlyList<string> fileNames, CancellationToken cancellationToken) =>
        _calls.Run<PrinterCommandsService, IReadOnlyList<PrintMetadata>>((printers, userId) => printers.GetFileDetails(userId, printerId, fileNames, cancellationToken), cancellationToken);

    public Task<PrintThumbnail?> GetThumbnailAsync(Guid printerId, string fileName, CancellationToken cancellationToken) =>
        _calls.Run<PrinterCommandsService, PrintThumbnail?>((printers, userId) => printers.GetThumbnail(userId, printerId, fileName, cancellationToken), cancellationToken);

    public Task PausePrintJobAsync(Guid printerId, CancellationToken cancellationToken) =>
        _calls.Run<PrinterCommandsService>((printers, userId) => printers.PausePrint(userId, printerId, cancellationToken), cancellationToken);

    public Task ResumePrintJobAsync(Guid printerId, CancellationToken cancellationToken) =>
        _calls.Run<PrinterCommandsService>((printers, userId) => printers.ResumePrint(userId, printerId, cancellationToken), cancellationToken);

    public Task SendConsoleCommandAsync(Guid printerId, string command, CancellationToken cancellationToken) =>
        _calls.Run<PrinterCommandsService>((printers, userId) => printers.SendConsoleCommand(userId, printerId, command, cancellationToken), cancellationToken);

    public Task StartPrintJobAsync(Guid printerId, string fileName, CancellationToken cancellationToken) =>
        _calls.Run<PrinterCommandsService>((printers, userId) => printers.StartPrint(userId, printerId, fileName, cancellationToken), cancellationToken);

    public Task StopPrintJobAsync(Guid printerId, CancellationToken cancellationToken) =>
        _calls.Run<PrinterCommandsService>((printers, userId) => printers.StopPrint(userId, printerId, cancellationToken), cancellationToken);

    public Task<ConnectionTestResult> TestConnectionAsync(
        Guid workspaceId,
        string connectorId,
        IReadOnlyDictionary<string, string> settings,
        Guid? printerId,
        Guid? agentId,
        CancellationToken cancellationToken) =>
        _calls.Run<PrinterCommandsService, ConnectionTestResult>(
            (printers, userId) => printers.TestConnection(userId, workspaceId, connectorId, settings, printerId, agentId, cancellationToken),
            cancellationToken);

    public Task UploadFileAsync(Guid printerId, Stream stream, string fileName, CancellationToken cancellationToken) =>
        _calls.Run<PrinterCommandsService>((printers, userId) => printers.UploadFile(userId, printerId, stream, fileName, cancellationToken), cancellationToken);
}
