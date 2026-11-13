using OctaGrid.Agent.Core.Connectors;
using OctaGrid.Agent.Core.Extensions;
using OctaGrid.Agent.Core.Link;
using OctaGrid.Extensions.Abstractions;
using OctaGrid.Extensions.Models;
using OctaGrid.Extensions.Models.Exceptions;
using OctaGrid.Protocol.AgentLink;
using OctaGrid.Protocol.AgentLink.Notifications;
using OctaGrid.Protocol.AgentLink.Requests;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Agent.Core.Tests;

/// <summary>The agent's side of the link: a request message in, the worker does it, a reply or an error out.</summary>
public sealed class AgentRequestDispatcherTests : IAsyncDisposable
{
    private static readonly Guid AgentId = Guid.NewGuid();

    private readonly FakeConnector _connector;
    private readonly AgentRequestDispatcher _dispatcher;
    private readonly AgentNotificationHub _hub = new();
    private readonly List<AgentNotification> _notifications = [];
    private readonly PrinterProfile _printer = new(Guid.NewGuid(), "acme.fake", new Dictionary<string, string> { ["url"] = "10.0.0.1" });
    private readonly List<StoringSession> _sessions = [];
    private readonly string _uploadFolder = Path.Combine(Path.GetTempPath(), "octagrid-tests", "dispatcher", Guid.NewGuid().ToString("N"));
    private readonly AgentUploads _uploads;
    private readonly AgentWorker _worker;

    public AgentRequestDispatcherTests()
    {
        _connector = new FakeConnector("acme.fake", _ =>
        {
            var session = new StoringSession();
            _sessions.Add(session);
            return session;
        });
        _worker = new AgentWorker(_hub, new ExtensionManager(new ConnectorRegistry().Add(_connector), new InMemoryStateStore()));
        _uploads = new AgentUploads(_uploadFolder);
        _dispatcher = new AgentRequestDispatcher(_worker, _uploads, _hub);
        _hub.Published += n =>
        {
            lock (_notifications)
            {
                _notifications.Add(n);
            }
        };
    }

    public async ValueTask DisposeAsync()
    {
        _dispatcher.Dispose();
        _uploads.Dispose();
        await _worker.DisposeAsync();
        if (Directory.Exists(_uploadFolder))
        {
            Directory.Delete(_uploadFolder, recursive: true);
        }
    }

    /// <summary>A session that keeps what is uploaded to it and can be told to refuse commands.</summary>
    private sealed class StoringSession : IPrinterSession, IPrintControl, IFileStorage, IConsoleInput
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<string> Calls { get; } = [];

        public Task Started => _started.Task;

        public ITelemetrySink? Telemetry { get; private set; }

        public Dictionary<string, byte[]> Stored { get; } = [];

        public async Task RunAsync(ITelemetrySink telemetry, CancellationToken cancellationToken)
        {
            Telemetry = telemetry;
            _started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ConnectionTestResult.Fail("No route to host"));

        public Task RequestStateAsync(CancellationToken cancellationToken) => Record("state");

        public Task StartPrintAsync(string fileName, CancellationToken cancellationToken) => Record($"start:{fileName}");

        public Task PausePrintAsync(CancellationToken cancellationToken) => Record("pause");

        public Task ResumePrintAsync(CancellationToken cancellationToken) => Record("resume");

        public Task StopPrintAsync(CancellationToken cancellationToken) => Record("stop");

        public Task<IReadOnlyList<PrintMetadata>> ListFilesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PrintMetadata>>([new PrintMetadata("a.gcode", 10, null, null, null)]);

        public async Task UploadFileAsync(Stream content, string fileName, CancellationToken cancellationToken)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            Stored[fileName] = copy.ToArray();
        }

        public Task DeleteFileAsync(string fileName, CancellationToken cancellationToken) => Record($"delete:{fileName}");

        public Task SendCommandAsync(string command, CancellationToken cancellationToken) =>
            throw new ValidationException("command", $"Unknown command \"{command}\".");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private Task Record(string call)
        {
            Calls.Add(call);
            return Task.CompletedTask;
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<AgentReplyEnvelope> Send(AgentRequest request, Guid? correlationId = null) =>
        _dispatcher.DispatchAsync(new AgentRequestEnvelope(AgentId, correlationId ?? Guid.NewGuid(), request), Ct);

    private async Task<TReply> Ask<TReply>(AgentRequest<TReply> request)
        where TReply : AgentReply
    {
        var answer = await Send(request);
        Assert.True(answer.Succeeded, answer.Error?.Detail);
        return Assert.IsType<TReply>(answer.Reply);
    }

    private async Task<StoringSession> StartPrinterAsync()
    {
        await Ask(new ApplyPrinterProfilesRequest([_printer]));
        var session = Assert.Single(_sessions);
        await session.Started;
        return session;
    }

    private IReadOnlyList<T> Notified<T>()
    {
        lock (_notifications)
        {
            return _notifications.OfType<T>().ToList();
        }
    }

    [Fact]
    public async Task The_reply_carries_the_correlation_id_of_the_request()
    {
        var correlationId = Guid.NewGuid();

        var answer = await Send(new GetConnectorsRequest(), correlationId);

        Assert.Equal(correlationId, answer.CorrelationId);
    }

    [Fact]
    public async Task Applying_profiles_starts_the_printers()
    {
        await StartPrinterAsync();

        Assert.Equal(_printer.PrinterId, Assert.Single(_connector.Created).PrinterId);
    }

    [Fact]
    public async Task Print_commands_reach_the_printer_session()
    {
        var session = await StartPrinterAsync();

        await Ask(new StartPrintRequest(_printer.PrinterId, "benchy.gcode"));
        await Ask(new PausePrintRequest(_printer.PrinterId));
        await Ask(new ResumePrintRequest(_printer.PrinterId));
        await Ask(new StopPrintRequest(_printer.PrinterId));
        await Ask(new DeletePrinterFileRequest(_printer.PrinterId, "old.gcode"));

        Assert.Equal(["start:benchy.gcode", "pause", "resume", "stop", "delete:old.gcode"], session.Calls);
    }

    [Fact]
    public async Task Queries_answer_with_what_the_worker_knows()
    {
        await StartPrinterAsync();

        var files = await Ask(new ListPrinterFilesRequest(_printer.PrinterId));
        var connectors = await Ask(new GetConnectorsRequest());
        var extensions = await Ask(new GetExtensionsRequest());
        var events = await Ask(new GetPrinterEventsRequest(_printer.PrinterId, null, 10));
        var console = await Ask(new GetPrinterConsoleRequest(_printer.PrinterId, null, 10));
        var folder = await Ask(new GetDeveloperFolderRequest());

        Assert.Equal("a.gcode", Assert.Single(files.Files).FileName);
        Assert.Equal("acme.fake", Assert.Single(connectors.Connectors).Id);
        Assert.Equal(1, Assert.Single(extensions.Extensions).ActivePrinters);
        Assert.NotNull(events.Page);
        Assert.NotNull(console.Page);
        Assert.Null(folder.Folder);
    }

    [Fact]
    public async Task A_connection_test_returns_the_connectors_verdict()
    {
        var reply = await Ask(new TestConnectionRequest("acme.fake", new Dictionary<string, string> { ["url"] = "10.0.0.1" }));

        Assert.False(reply.Result.Success);
        Assert.Equal("No route to host", reply.Result.Error);
    }

    [Fact]
    public async Task A_file_uploaded_in_pieces_is_stored_on_the_printer_and_the_temporary_file_is_removed()
    {
        var session = await StartPrinterAsync();
        var content = Enumerable.Range(0, 150_000).Select(i => (byte)i).ToArray();

        var started = await Ask(new BeginUploadRequest(content.Length));
        var sequence = 0;
        foreach (var piece in content.Chunk(started.MaxChunkSize))
        {
            await Ask(new UploadChunkRequest(started.UploadId, sequence++, piece));
        }

        await Ask(new StorePrinterFileRequest(_printer.PrinterId, started.UploadId, "part.gcode"));

        Assert.True(sequence > 1);
        Assert.Equal(content, session.Stored["part.gcode"]);
        Assert.Empty(Directory.GetFiles(_uploadFolder));
    }

    [Fact]
    public async Task An_error_the_app_understands_comes_back_as_an_error_not_an_exception()
    {
        await StartPrinterAsync();

        var answer = await Send(new SendConsoleCommandRequest(_printer.PrinterId, "FOO"));

        Assert.False(answer.Succeeded);
        Assert.Null(answer.Reply);
        Assert.Equal(ValidationException.Uri, answer.Error!.TypeUri);
        Assert.Contains("FOO", answer.Error.Errors!["command"]);
        Assert.IsType<ValidationException>(answer.Error.ToException());
    }

    [Fact]
    public async Task Using_an_unknown_upload_is_an_error()
    {
        await StartPrinterAsync();

        var answer = await Send(new StorePrinterFileRequest(_printer.PrinterId, Guid.NewGuid(), "part.gcode"));

        Assert.False(answer.Succeeded);
        Assert.Equal(ValidationException.Uri, answer.Error!.TypeUri);
    }

    [Fact]
    public async Task Cancellation_is_thrown_not_turned_into_an_error()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _dispatcher.DispatchAsync(
            new AgentRequestEnvelope(AgentId, Guid.NewGuid(), new ApplyPrinterProfilesRequest([_printer])), cancelled.Token));
    }

    [Fact]
    public async Task Telemetry_and_recorded_events_are_published_as_notifications()
    {
        var session = await StartPrinterAsync();

        session.Telemetry!.Send(new PrinterTelemetry(_printer.PrinterId) { State = UniversalPrinterState.Printing });

        Assert.Contains(Notified<PrinterTelemetryReported>(), n => n.Telemetry.State == UniversalPrinterState.Printing);
        Assert.Contains(Notified<PrinterEventRecorded>(), n => n.PrinterId == _printer.PrinterId);
    }

    [Fact]
    public async Task Turning_an_extension_off_announces_the_new_connector_list()
    {
        await StartPrinterAsync();

        await Ask(new SetExtensionEnabledRequest("acme.fake", false));

        Assert.Empty(Assert.Single(Notified<ConnectorsChanged>()).Connectors);
        Assert.Empty((await Ask(new GetConnectorsRequest())).Connectors);
    }

    [Fact]
    public async Task After_the_dispatcher_is_disposed_it_forwards_nothing_more()
    {
        var session = await StartPrinterAsync();
        _dispatcher.Dispose();
        var before = Notified<PrinterEventRecorded>().Count;

        session.Telemetry!.Send(new PrinterTelemetry(_printer.PrinterId) { State = UniversalPrinterState.Paused });

        Assert.Equal(before, Notified<PrinterEventRecorded>().Count);
    }

    [Fact]
    public void A_subscriber_that_throws_does_not_stop_the_others()
    {
        var hub = new AgentNotificationHub();
        var received = 0;
        hub.Published += _ => throw new InvalidOperationException("broken listener");
        hub.Published += _ => received++;

        hub.Send(new PrinterTelemetry(Guid.NewGuid()));

        Assert.Equal(1, received);
    }
}
