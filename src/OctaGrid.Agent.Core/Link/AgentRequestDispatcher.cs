using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Extensions.Models.Exceptions;
using OctaGrid.Protocol.AgentLink;
using OctaGrid.Protocol.AgentLink.Notifications;
using OctaGrid.Protocol.AgentLink.Replies;
using OctaGrid.Protocol.AgentLink.Requests;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Agent.Core.Link;

/// <summary>
/// The agent's side of the link to a backend: takes a request message, does it with the
/// <see cref="AgentWorker"/> and answers with a reply or an error. It also forwards what the worker reports
/// on its own to the <see cref="AgentNotificationHub"/>. The transport (in process today, SignalR later)
/// calls <see cref="DispatchAsync"/> and never the worker.
/// </summary>
public sealed class AgentRequestDispatcher : IDisposable
{
    /// <summary>The extension an uploaded package gets, so the installer treats it as a package file.</summary>
    private const string PackageExtension = ".octaext";

    private static readonly AgentAck Ack = new();

    private readonly AgentNotificationHub _hub;
    private readonly ILogger<AgentRequestDispatcher> _logger;
    private readonly AgentUploads _uploads;
    private readonly AgentWorker _worker;

    public AgentRequestDispatcher(
        AgentWorker worker, AgentUploads uploads, AgentNotificationHub hub, ILogger<AgentRequestDispatcher>? logger = null)
    {
        _worker = worker;
        _uploads = uploads;
        _hub = hub;
        _logger = logger ?? NullLogger<AgentRequestDispatcher>.Instance;

        _worker.EventRecorded += OnEventRecorded;
        _worker.ConsoleLineRecorded += OnConsoleLineRecorded;
        _worker.ConnectorsChanged += OnConnectorsChanged;
    }

    /// <summary>
    /// Handles one request. A failure the app understands (<see cref="AppException"/>) becomes an error in the
    /// reply; cancellation and unexpected exceptions are thrown, for the transport to deal with.
    /// </summary>
    public async Task<AgentReplyEnvelope> DispatchAsync(AgentRequestEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        // Only the kind of request is logged: several requests carry secrets or file content.
        _logger.LogDebug("Handling {RequestType} ({CorrelationId})", envelope.Request.GetType().Name, envelope.CorrelationId);
        try
        {
            return new AgentReplyEnvelope(envelope.CorrelationId, await HandleAsync(envelope.Request, cancellationToken));
        }
        catch (AppException ex)
        {
            _logger.LogInformation("{RequestType} ({CorrelationId}) was refused: {ErrorTitle}",
                envelope.Request.GetType().Name, envelope.CorrelationId, ex.Title);
            return new AgentReplyEnvelope(envelope.CorrelationId, Reply: null, AgentError.From(ex));
        }
    }

    public void Dispose()
    {
        _worker.EventRecorded -= OnEventRecorded;
        _worker.ConsoleLineRecorded -= OnConsoleLineRecorded;
        _worker.ConnectorsChanged -= OnConnectorsChanged;
    }

    private async Task<AgentReply> HandleAsync(AgentRequest request, CancellationToken ct)
    {
        switch (request)
        {
            case StartPrintRequest r:
                await _worker.StartPrintJobAsync(r.PrinterId, r.FileName, ct);
                return Ack;
            case PausePrintRequest r:
                await _worker.PausePrintJobAsync(r.PrinterId, ct);
                return Ack;
            case ResumePrintRequest r:
                await _worker.ResumePrintJobAsync(r.PrinterId, ct);
                return Ack;
            case StopPrintRequest r:
                await _worker.StopPrintJobAsync(r.PrinterId, ct);
                return Ack;

            case ListPrinterFilesRequest r:
                return new PrinterFilesReply((await _worker.GetPrinterFilesAsync(r.PrinterId, ct)).ToList());
            case GetPrintThumbnailRequest r:
                return new PrintThumbnailReply(await _worker.GetThumbnailAsync(r.PrinterId, r.FileName, ct));
            case DeletePrinterFileRequest r:
                await _worker.DeleteFileAsync(r.PrinterId, r.FileName, ct);
                return Ack;
            case StorePrinterFileRequest r:
                using (var upload = await _uploads.CompleteAsync(r.UploadId, extension: null, ct))
                {
                    await using var content = upload.OpenRead();
                    await _worker.UploadFileAsync(r.PrinterId, content, r.FileName, ct);
                }

                return Ack;

            case GetPrinterConsoleRequest r:
                return new PrinterConsoleReply(_worker.GetPrinterConsole(r.PrinterId, r.AfterSequence, r.Limit));
            case SendConsoleCommandRequest r:
                await _worker.SendConsoleCommandAsync(r.PrinterId, r.Command, ct);
                return Ack;
            case GetPrinterEventsRequest r:
                return new PrinterEventsReply(_worker.GetPrinterEvents(r.PrinterId, r.AfterSequence, r.Limit));

            case RequestPrinterStateRequest r:
                await _worker.TriggerInitialStateAsync(r.PrinterIds, ct);
                return Ack;
            case ApplyPrinterProfilesRequest r:
                await _worker.ApplyProfilesAsync(r.Profiles, ct);
                return Ack;
            case TestConnectionRequest r:
                return new ConnectionTestReply(await _worker.TestConnectionAsync(r.ConnectorId, r.Settings, ct));

            case GetConnectorsRequest:
                return new ConnectorsReply(_worker.ConnectorDescriptors);
            case GetExtensionsRequest:
                return new ExtensionsReply(_worker.GetExtensions());
            case SetExtensionEnabledRequest r:
                await _worker.SetExtensionEnabledAsync(r.ExtensionId, r.Enabled, ct);
                return Ack;
            case InstallExtensionRequest r:
                using (var upload = await _uploads.CompleteAsync(r.UploadId, PackageExtension, ct))
                {
                    // Unpacking is blocking file work, so it is kept off the caller's thread.
                    var path = upload.Path;
                    return new ExtensionChangeReply(await Task.Run(() => _worker.InstallExtension(path), ct));
                }

            case ApproveExtensionRequest r:
                return new ExtensionChangeReply(await _worker.ApproveExtensionAsync(r.ExtensionId, ct));
            case UninstallExtensionRequest r:
                return new ExtensionChangeReply(await _worker.UninstallExtensionAsync(r.ExtensionId, ct));
            case GetDeveloperFolderRequest:
                return new DeveloperFolderReply(_worker.DeveloperFolder);
            case SetDeveloperFolderRequest r:
                return new ExtensionChangeReply(await _worker.SetDeveloperFolderAsync(r.Folder, ct));
            case ReloadDeveloperExtensionsRequest:
                return new ExtensionChangeReply(await _worker.ReloadDeveloperExtensionsAsync(ct));

            case BeginUploadRequest r:
                return new UploadStartedReply(_uploads.Begin(r.Length), AgentUploads.MaxChunkSize);
            case UploadChunkRequest r:
                await _uploads.AppendAsync(r.UploadId, r.Sequence, r.Data, ct);
                return Ack;
            case AbortUploadRequest r:
                _uploads.Abort(r.UploadId);
                return Ack;

            default:
                // A request from a newer backend: say so instead of failing in an unexpected way.
                throw new ValidationException("request", $"This agent does not understand '{request.GetType().Name}'. Update the agent.");
        }
    }

    private void OnConnectorsChanged() => _hub.Publish(new ConnectorsChanged(_worker.ConnectorDescriptors));

    private void OnConsoleLineRecorded(PrinterConsoleLineRecorded recorded) => _hub.Publish(recorded);

    private void OnEventRecorded(PrinterEventRecorded recorded) => _hub.Publish(recorded);
}
