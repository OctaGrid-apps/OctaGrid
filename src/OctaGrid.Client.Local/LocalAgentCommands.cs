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
    private readonly LocalConnectorCache _connectors;
    private readonly LocalAgent _localAgent;

    public LocalAgentCommands(LocalCalls calls, LocalAgent localAgent, LocalConnectorCache connectors)
    {
        _calls = calls;
        _localAgent = localAgent;
        _connectors = connectors;
    }

    public IReadOnlyList<ConnectorDescriptor> Connectors => _connectors.Connectors;

    public Task DeleteFileAsync(Guid deviceId, string fileName, CancellationToken cancellationToken) =>
        _calls.Run<PrinterCommandsService>((printers, userId) => printers.DeleteFile(userId, deviceId, fileName, cancellationToken), cancellationToken);

    public Task<ConsolePage> GetPrinterConsoleAsync(Guid printerId, long? afterSequence, int limit, CancellationToken cancellationToken) =>
        _calls.Run<PrinterCommandsService, ConsolePage>((printers, userId) => printers.GetConsole(userId, printerId, afterSequence, limit, cancellationToken), cancellationToken);

    public Task<PrinterEventPage> GetPrinterEventsAsync(Guid printerId, long? afterSequence, int limit, CancellationToken cancellationToken) =>
        _calls.Run<PrinterCommandsService, PrinterEventPage>((printers, userId) => printers.GetEvents(userId, printerId, afterSequence, limit, cancellationToken), cancellationToken);

    public Task<IEnumerable<PrintMetadata>> GetPrinterFilesAsync(Guid deviceId, CancellationToken cancellationToken) =>
        _calls.Run<PrinterCommandsService, IEnumerable<PrintMetadata>>(async (printers, userId) => await printers.GetFiles(userId, deviceId, cancellationToken), cancellationToken);

    public Task<PrintThumbnail?> GetThumbnailAsync(Guid printerId, string fileName, CancellationToken cancellationToken) =>
        _calls.Run<PrinterCommandsService, PrintThumbnail?>((printers, userId) => printers.GetThumbnail(userId, printerId, fileName, cancellationToken), cancellationToken);

    public Task PausePrintJobAsync(Guid deviceId, CancellationToken cancellationToken) =>
        _calls.Run<PrinterCommandsService>((printers, userId) => printers.PausePrint(userId, deviceId, cancellationToken), cancellationToken);

    public Task ResumePrintJobAsync(Guid deviceId, CancellationToken cancellationToken) =>
        _calls.Run<PrinterCommandsService>((printers, userId) => printers.ResumePrint(userId, deviceId, cancellationToken), cancellationToken);

    public Task SendConsoleCommandAsync(Guid printerId, string command, CancellationToken cancellationToken) =>
        _calls.Run<PrinterCommandsService>((printers, userId) => printers.SendConsoleCommand(userId, printerId, command, cancellationToken), cancellationToken);

    public Task StartPrintJobAsync(Guid deviceId, string fileName, CancellationToken cancellationToken) =>
        _calls.Run<PrinterCommandsService>((printers, userId) => printers.StartPrint(userId, deviceId, fileName, cancellationToken), cancellationToken);

    public Task StopPrintJobAsync(Guid deviceId, CancellationToken cancellationToken) =>
        _calls.Run<PrinterCommandsService>((printers, userId) => printers.StopPrint(userId, deviceId, cancellationToken), cancellationToken);

    // A new printer, or a saved one without an agent, is tested by the agent it will get: in Local mode, the local one.
    public Task<ConnectionTestResult> TestConnectionAsync(
        string connectorId, IReadOnlyDictionary<string, string> settings, Guid? printerId, CancellationToken cancellationToken) =>
        _calls.Run<PrinterCommandsService, ConnectionTestResult>(
            (printers, userId) => printers.TestConnection(userId, connectorId, settings, printerId, _localAgent.Id, cancellationToken), cancellationToken);

    public Task UploadFileAsync(Guid deviceId, Stream stream, string fileName, CancellationToken cancellationToken) =>
        _calls.Run<PrinterCommandsService>((printers, userId) => printers.UploadFile(userId, deviceId, stream, fileName, cancellationToken), cancellationToken);
}
