using Microsoft.Extensions.Logging;
using OctaGrid.Agent.Core.Connectors;
using OctaGrid.Agent.Core.Files;
using OctaGrid.Extensions.Abstractions;
using OctaGrid.Extensions.Models;
using OctaGrid.Extensions.Models.Exceptions;

namespace OctaGrid.Agent.Core.Tests;

public class PrinterOrchestratorTests
{
    private const string Id = "acme.fake";

    private static PrinterProfile Profile(Guid? id = null, string connectorId = Id, string url = "10.0.0.1",
        string? apiKey = null)
    {
        var settings = new Dictionary<string, string> { ["url"] = url };
        if (apiKey is not null)
        {
            settings["apiKey"] = apiKey;
        }

        return new PrinterProfile(id ?? Guid.NewGuid(), connectorId, settings);
    }

    private static (PrinterOrchestrator Orchestrator, FakeConnector Connector, List<FakeSession> Sessions, FakeTelemetry Telemetry)
        Create()
    {
        var sessions = new List<FakeSession>();
        var connector = new FakeConnector(Id, _ =>
        {
            var session = new FakeSession();
            sessions.Add(session);
            return session;
        });
        var telemetry = new FakeTelemetry();
        var orchestrator = new PrinterOrchestrator(
            new ConnectorRegistry().Add(connector), telemetry, dataRoot: Path.Combine(Path.GetTempPath(), "octagrid-tests"));
        return (orchestrator, connector, sessions, telemetry);
    }

    /// <summary>A session that never ends, whatever it is told: a connector stuck in a blocking call.</summary>
    private sealed class StuckSession : IPrinterSession
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public Task RunAsync(ITelemetrySink telemetry, CancellationToken cancellationToken)
        {
            _started.TrySetResult();
            return new TaskCompletionSource().Task;
        }

        public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ConnectionTestResult.Ok());


        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>A session that reports a console and writes one line as soon as it runs.</summary>
    private sealed class ConsoleSession : IPrinterSession, IConsoleOutput
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private IConsoleSink? _sink;

        public bool SinkWasSetBeforeRun { get; private set; }

        public Task Started => _started.Task;

        public void SetConsoleSink(IConsoleSink sink) => _sink = sink;

        public async Task RunAsync(ITelemetrySink telemetry, CancellationToken cancellationToken)
        {
            SinkWasSetBeforeRun = _sink is not null;
            _sink?.WriteLine("hello from the printer");
            _started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ConnectionTestResult.Ok());


        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task A_session_that_reports_a_console_gets_its_sink_before_it_runs_and_its_lines_are_kept()
    {
        var session = new ConsoleSession();
        var log = new Events.PrinterConsoleLog();
        var orchestrator = new PrinterOrchestrator(
            new ConnectorRegistry().Add(new FakeConnector(Id, _ => session)), new FakeTelemetry(),
            dataRoot: Path.Combine(Path.GetTempPath(), "octagrid-tests"), console: log);
        var profile = Profile();

        await orchestrator.ApplyProfilesAsync([profile]);
        await session.Started;

        Assert.True(session.SinkWasSetBeforeRun);
        Assert.Equal(["hello from the printer"], log.Get(profile.PrinterId, null, 10).Lines.Select(l => l.Text));
        await orchestrator.DisposeAsync();
    }

    /// <summary>A session that takes commands and writes an answer to its console for each one.</summary>
    private sealed class CommandSession : IPrinterSession, IConsoleOutput, IConsoleInput
    {
        private IConsoleSink? _sink;

        public List<string> Received { get; } = [];

        public Exception? Failure { get; set; }

        public void SetConsoleSink(IConsoleSink sink) => _sink = sink;

        public Task SendCommandAsync(string command, CancellationToken cancellationToken)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Received.Add(command);
            _sink?.WriteLine("ok");
            return Task.CompletedTask;
        }

        public Task RunAsync(ITelemetrySink telemetry, CancellationToken cancellationToken) =>
            Task.Delay(Timeout.Infinite, cancellationToken);

        public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ConnectionTestResult.Ok());


        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static async Task<(PrinterOrchestrator Orchestrator, CommandSession Session, Events.PrinterConsoleLog Log, Guid PrinterId)>
        StartCommandSession()
    {
        var session = new CommandSession();
        var log = new Events.PrinterConsoleLog();
        var orchestrator = new PrinterOrchestrator(
            new ConnectorRegistry().Add(new FakeConnector(Id, _ => session)), new FakeTelemetry(),
            dataRoot: Path.Combine(Path.GetTempPath(), "octagrid-tests"), console: log);
        var profile = Profile();
        await orchestrator.ApplyProfilesAsync([profile]);
        return (orchestrator, session, log, profile.PrinterId);
    }

    [Fact]
    public async Task A_command_is_shown_in_the_console_before_it_is_sent_and_the_answer_follows()
    {
        var (orchestrator, session, log, printerId) = await StartCommandSession();

        await orchestrator.SendConsoleCommandAsync(printerId, "  M105  ", CancellationToken.None);

        Assert.Equal(["M105"], session.Received);
        Assert.Equal(["> M105", "ok"], log.Get(printerId, null, 10).Lines.Select(l => l.Text));
        await orchestrator.DisposeAsync();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("M105\nM104 S0")]
    [InlineData("M105\u0000")]
    public async Task A_command_that_is_not_one_line_of_plain_text_never_reaches_the_printer(string command)
    {
        var (orchestrator, session, log, printerId) = await StartCommandSession();

        await Assert.ThrowsAsync<OctaGrid.Extensions.Models.Exceptions.ValidationException>(
            () => orchestrator.SendConsoleCommandAsync(printerId, command, CancellationToken.None));

        Assert.Empty(session.Received);
        Assert.Empty(log.Get(printerId, null, 10).Lines);
        await orchestrator.DisposeAsync();
    }

    [Fact]
    public async Task A_command_that_is_too_long_never_reaches_the_printer()
    {
        var (orchestrator, session, _, printerId) = await StartCommandSession();

        await Assert.ThrowsAsync<OctaGrid.Extensions.Models.Exceptions.ValidationException>(() => orchestrator.SendConsoleCommandAsync(
            printerId, new string('x', OctaGrid.Extensions.Models.ConsoleCommandRules.MaxLength + 1), CancellationToken.None));

        Assert.Empty(session.Received);
        await orchestrator.DisposeAsync();
    }

    /// <summary>Keeps everything that is logged, with its arguments, so a test can look for what must not be in it.</summary>
    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        public List<string> Messages { get; } = [];

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger : ILogger
        {
            private readonly List<string> _messages;

            public CapturingLogger(List<string> messages)
            {
                _messages = messages;
            }

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                _messages.Add(formatter(state, exception));
        }
    }

    [Fact]
    public async Task The_log_names_a_command_but_never_shows_its_arguments_which_can_hold_a_secret()
    {
        var session = new CommandSession();
        var loggerFactory = new CapturingLoggerFactory();
        var orchestrator = new PrinterOrchestrator(
            new ConnectorRegistry().Add(new FakeConnector(Id, _ => session)), new FakeTelemetry(), loggerFactory,
            dataRoot: Path.Combine(Path.GetTempPath(), "octagrid-tests"), console: new Events.PrinterConsoleLog());
        var profile = Profile();
        await orchestrator.ApplyProfilesAsync([profile]);

        await orchestrator.SendConsoleCommandAsync(profile.PrinterId, "M587 S\"home\" P\"hunter2\"", CancellationToken.None);

        Assert.Equal(["M587 S\"home\" P\"hunter2\""], session.Received);
        Assert.Contains(loggerFactory.Messages, m => m.Contains("M587", StringComparison.Ordinal));
        Assert.DoesNotContain(loggerFactory.Messages, m => m.Contains("hunter2", StringComparison.Ordinal));
        await orchestrator.DisposeAsync();
    }

    [Fact]
    public async Task The_echo_of_a_command_is_shown_even_while_the_printer_floods_the_console()
    {
        var session = new CommandSession();
        var log = new Events.PrinterConsoleLog(options: new Events.PrinterConsoleLogOptions(Burst: 2, LinesPerSecond: 1));
        var orchestrator = new PrinterOrchestrator(
            new ConnectorRegistry().Add(new FakeConnector(Id, _ => session)), new FakeTelemetry(),
            dataRoot: Path.Combine(Path.GetTempPath(), "octagrid-tests"), console: log);
        var profile = Profile();
        await orchestrator.ApplyProfilesAsync([profile]);
        var sink = log.CreateSink(profile.PrinterId);
        for (var i = 0; i < 10; i++)
        {
            sink.WriteLine($"spam {i}");
        }

        await orchestrator.SendConsoleCommandAsync(profile.PrinterId, "M112", CancellationToken.None);

        Assert.Contains(log.Get(profile.PrinterId, null, 50).Lines, l => l.Text == "> M112");
        await orchestrator.DisposeAsync();
    }

    [Fact]
    public async Task A_printer_without_a_running_session_cannot_take_a_command()
    {
        var (orchestrator, _, _, _) = await StartCommandSession();

        await Assert.ThrowsAsync<OctaGrid.Extensions.Models.Exceptions.NotFoundException>(
            () => orchestrator.SendConsoleCommandAsync(Guid.NewGuid(), "M105", CancellationToken.None));
        await orchestrator.DisposeAsync();
    }

    [Fact]
    public async Task A_connector_that_cannot_take_commands_refuses_with_a_reason_the_user_can_read()
    {
        var session = new ConsoleSession();
        var log = new Events.PrinterConsoleLog();
        var orchestrator = new PrinterOrchestrator(
            new ConnectorRegistry().Add(new FakeConnector(Id, _ => session)), new FakeTelemetry(),
            dataRoot: Path.Combine(Path.GetTempPath(), "octagrid-tests"), console: log);
        var profile = Profile();
        await orchestrator.ApplyProfilesAsync([profile]);

        var error = await Assert.ThrowsAsync<OctaGrid.Extensions.Models.Exceptions.ValidationException>(
            () => orchestrator.SendConsoleCommandAsync(profile.PrinterId, "M105", CancellationToken.None));

        Assert.Equal("This printer's connector cannot send commands.", error.Message);
        Assert.DoesNotContain(log.Get(profile.PrinterId, null, 10).Lines, l => l.Text.StartsWith('>'));
        await orchestrator.DisposeAsync();
    }

    [Fact]
    public async Task A_printer_that_rejects_a_command_passes_the_reason_on()
    {
        var (orchestrator, session, _, printerId) = await StartCommandSession();
        session.Failure = new OctaGrid.Extensions.Models.Exceptions.ValidationException("command", "Unknown command \"FOO\".");

        var error = await Assert.ThrowsAsync<OctaGrid.Extensions.Models.Exceptions.ValidationException>(
            () => orchestrator.SendConsoleCommandAsync(printerId, "FOO", CancellationToken.None));

        Assert.Equal("Unknown command \"FOO\".", error.Message);
        await orchestrator.DisposeAsync();
    }

    [Fact]
    public async Task Console_output_is_dropped_when_the_host_keeps_no_console()
    {
        var session = new ConsoleSession();
        var orchestrator = new PrinterOrchestrator(
            new ConnectorRegistry().Add(new FakeConnector(Id, _ => session)), new FakeTelemetry(),
            dataRoot: Path.Combine(Path.GetTempPath(), "octagrid-tests"));

        await orchestrator.ApplyProfilesAsync([Profile()]);
        await session.Started;

        Assert.False(session.SinkWasSetBeforeRun);
        await orchestrator.DisposeAsync();
    }

    [Fact]
    public async Task A_session_that_ignores_the_stop_does_not_hold_up_the_change_of_printers()
    {
        var stuck = new StuckSession();
        var connector = new FakeConnector(Id, _ => stuck);
        var orchestrator = new PrinterOrchestrator(
            new ConnectorRegistry().Add(connector), new FakeTelemetry(),
            dataRoot: Path.Combine(Path.GetTempPath(), "octagrid-tests"), stopTimeout: TimeSpan.FromMilliseconds(50));
        await orchestrator.ApplyProfilesAsync([Profile()]);
        await stuck.Started;

        // Without the limit this never completes; the wait only fails the test instead of hanging it.
        await orchestrator.ApplyProfilesAsync([]).WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        Assert.Equal(0, orchestrator.ActiveSessionCount(Id));
    }

    [Fact]
    public async Task Starts_a_session_with_the_profile_and_the_telemetry_sink()
    {
        var (orchestrator, connector, sessions, telemetry) = Create();
        var profile = Profile(apiKey: "secret");

        await orchestrator.ApplyProfilesAsync([profile]);
        await sessions.Single().Started;

        Assert.Same(profile, Assert.Single(connector.Created));
        Assert.Same(telemetry, sessions.Single().Telemetry);
        await orchestrator.DisposeAsync();
    }

    [Fact]
    public async Task Does_not_restart_when_an_equal_profile_arrives_with_new_dictionary_instances()
    {
        var (orchestrator, connector, sessions, _) = Create();
        var id = Guid.NewGuid();

        await orchestrator.ApplyProfilesAsync([Profile(id, apiKey: "k")]);
        await orchestrator.ApplyProfilesAsync([Profile(id, apiKey: "k")]);

        Assert.Single(connector.Created);
        Assert.False(sessions.Single().Disposed);
        await orchestrator.DisposeAsync();
    }

    [Fact]
    public async Task Restarts_when_the_endpoint_or_settings_change()
    {
        var (orchestrator, connector, sessions, _) = Create();
        var id = Guid.NewGuid();

        await orchestrator.ApplyProfilesAsync([Profile(id, url: "10.0.0.1")]);
        await orchestrator.ApplyProfilesAsync([Profile(id, url: "10.0.0.2")]);
        await orchestrator.ApplyProfilesAsync([Profile(id, url: "10.0.0.2", apiKey: "new")]);

        Assert.Equal(3, connector.Created.Count);
        Assert.True(sessions[0].Disposed);
        Assert.True(sessions[1].Disposed);
        Assert.False(sessions[2].Disposed);
        await orchestrator.DisposeAsync();
    }

    [Fact]
    public async Task Stops_sessions_for_printers_that_are_no_longer_wanted()
    {
        var (orchestrator, _, sessions, _) = Create();

        await orchestrator.ApplyProfilesAsync([Profile()]);
        await orchestrator.ApplyProfilesAsync([]);

        Assert.True(sessions.Single().Disposed);
    }

    [Fact]
    public async Task Disposing_stops_all_sessions()
    {
        var (orchestrator, _, sessions, _) = Create();
        await orchestrator.ApplyProfilesAsync([Profile(), Profile()]);

        await orchestrator.DisposeAsync();

        Assert.All(sessions, s => Assert.True(s.Disposed));
    }

    [Fact]
    public async Task A_printer_with_a_missing_connector_does_not_stop_the_others()
    {
        var (orchestrator, connector, _, _) = Create();
        var good = Profile();

        await orchestrator.ApplyProfilesAsync([Profile(connectorId: "acme.missing"), good]);

        Assert.Same(good, Assert.Single(connector.Created));
        await orchestrator.DisposeAsync();
    }

    [Fact]
    public async Task Routes_operations_to_the_session_capabilities()
    {
        var (orchestrator, _, sessions, _) = Create();
        var profile = Profile();
        var ct = CancellationToken.None;
        await orchestrator.ApplyProfilesAsync([profile]);

        await orchestrator.StartPrintJobAsync(profile.PrinterId, "a.gcode", null, ct);
        await orchestrator.PausePrintJobAsync(profile.PrinterId, ct);
        await orchestrator.ResumePrintJobAsync(profile.PrinterId, ct);
        await orchestrator.StopPrintJobAsync(profile.PrinterId, ct);
        await orchestrator.UploadFileAsync(profile.PrinterId, new MemoryStream(), "b.gcode", ct);
        await orchestrator.DeleteFileAsync(profile.PrinterId, "c.gcode", ct);
        var files = await orchestrator.GetPrinterFilesAsync(profile.PrinterId, ct);
        var thumbnail = await orchestrator.GetThumbnailAsync(profile.PrinterId, "a.gcode", ct);

        Assert.Equal(
            ["start:a.gcode", "pause", "resume", "stop", "upload:b.gcode", "delete:c.gcode", "list", "thumb:a.gcode"],
            sessions.Single().Calls);
        Assert.Single(files);
        Assert.Equal("image/png", thumbnail?.ContentType);
        await orchestrator.DisposeAsync();
    }

    private sealed class PlanningSession : IPrinterSession, IPrintControl, IPrintStartOptions
    {
        public List<string> Calls { get; } = [];

        public PrintStartChoices? Choices { get; private set; }

        public Task RunAsync(ITelemetrySink telemetry, CancellationToken cancellationToken) =>
            Task.Delay(Timeout.Infinite, cancellationToken);

        public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ConnectionTestResult.Ok());

        public Task<PrintStartPlan> GetStartPlanAsync(string fileName, CancellationToken cancellationToken)
        {
            Calls.Add($"plan:{fileName}");
            return Task.FromResult(new PrintStartPlan(
                fileName,
                [new PrintPlate(1, Thumbnail: new PrintThumbnail("image/png", [1])), new PrintPlate(2, Thumbnail: new PrintThumbnail("image/png", [2]))],
                []));
        }

        public Task StartPrintAsync(string fileName, PrintStartChoices choices, CancellationToken cancellationToken)
        {
            Calls.Add($"start-with-choices:{fileName}");
            Choices = choices;
            return Task.CompletedTask;
        }

        public Task StartPrintAsync(string fileName, CancellationToken cancellationToken)
        {
            Calls.Add($"start:{fileName}");
            return Task.CompletedTask;
        }

        public Task PausePrintAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ResumePrintAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopPrintAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task A_connector_with_start_choices_gets_asked_for_the_plan_and_given_the_choices()
    {
        var session = new PlanningSession();
        var orchestrator = new PrinterOrchestrator(
            new ConnectorRegistry().Add(new FakeConnector(Id, _ => session)), new FakeTelemetry(),
            dataRoot: Path.Combine(Path.GetTempPath(), "octagrid-tests"));
        var profile = Profile();
        var choices = new PrintStartChoices(2);
        var ct = CancellationToken.None;
        await orchestrator.ApplyProfilesAsync([profile]);

        var plan = await orchestrator.GetPrintStartPlanAsync(profile.PrinterId, "a.gcode.3mf", ct);
        await orchestrator.StartPrintJobAsync(profile.PrinterId, "a.gcode.3mf", choices, ct);

        Assert.Equal(2, plan?.Plates.Count);
        Assert.Equal(["plan:a.gcode.3mf", "start-with-choices:a.gcode.3mf"], session.Calls);
        Assert.Same(choices, session.Choices);
        await orchestrator.DisposeAsync();
    }

    [Fact]
    public async Task A_connector_without_start_choices_has_no_plan_and_starts_as_before()
    {
        var (orchestrator, _, sessions, _) = Create();
        var profile = Profile();
        var ct = CancellationToken.None;
        await orchestrator.ApplyProfilesAsync([profile]);

        Assert.Null(await orchestrator.GetPrintStartPlanAsync(profile.PrinterId, "a.gcode", ct));
        await orchestrator.StartPrintJobAsync(profile.PrinterId, "a.gcode", new PrintStartChoices(), ct);

        Assert.Equal(["start:a.gcode"], sessions.Single().Calls);
        await orchestrator.DisposeAsync();
    }

    [Fact]
    public async Task A_started_print_keeps_the_picture_of_the_plate_the_user_chose()
    {
        var root = Path.Combine(Path.GetTempPath(), "octagrid-file-cache-tests", Guid.NewGuid().ToString("N"));
        var orchestrator = new PrinterOrchestrator(
            new ConnectorRegistry().Add(new FakeConnector(Id, _ => new PlanningSession())), new FakeTelemetry(),
            dataRoot: Path.Combine(Path.GetTempPath(), "octagrid-tests"),
            fileCache: new PrinterFileCache(root));
        var profile = Profile();
        var ct = CancellationToken.None;
        try
        {
            await orchestrator.ApplyProfilesAsync([profile]);

            await orchestrator.GetPrintStartPlanAsync(profile.PrinterId, "a.gcode.3mf", ct);
            await orchestrator.StartPrintJobAsync(profile.PrinterId, "a.gcode.3mf", new PrintStartChoices(2), ct);

            // The printer reports the job by the file's name without its extension.
            Assert.Equal([2], (await orchestrator.GetJobPictureAsync(profile.PrinterId, "a", ct))?.Data);
        }
        finally
        {
            await orchestrator.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_print_started_without_a_plan_keeps_the_files_thumbnail()
    {
        var root = Path.Combine(Path.GetTempPath(), "octagrid-file-cache-tests", Guid.NewGuid().ToString("N"));
        var session = new FakeSession();
        var orchestrator = new PrinterOrchestrator(
            new ConnectorRegistry().Add(new FakeConnector(Id, _ => session)), new FakeTelemetry(),
            dataRoot: Path.Combine(Path.GetTempPath(), "octagrid-tests"),
            fileCache: new PrinterFileCache(root));
        var profile = Profile();
        var ct = CancellationToken.None;
        try
        {
            await orchestrator.ApplyProfilesAsync([profile]);

            await orchestrator.StartPrintJobAsync(profile.PrinterId, "a.gcode", null, ct);

            Assert.Equal(["start:a.gcode", "thumb:a.gcode"], session.Calls);
            Assert.Equal([9], (await orchestrator.GetJobPictureAsync(profile.PrinterId, "a", ct))?.Data);
        }
        finally
        {
            await orchestrator.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_thumbnail_kept_in_the_file_cache_is_not_asked_of_the_printer_again()
    {
        var root = Path.Combine(Path.GetTempPath(), "octagrid-file-cache-tests", Guid.NewGuid().ToString("N"));
        var sessions = new List<FakeSession>();
        var connector = new FakeConnector(Id, _ =>
        {
            var session = new FakeSession();
            sessions.Add(session);
            return session;
        });
        var orchestrator = new PrinterOrchestrator(
            new ConnectorRegistry().Add(connector), new FakeTelemetry(),
            dataRoot: Path.Combine(Path.GetTempPath(), "octagrid-tests"),
            fileCache: new PrinterFileCache(root));
        var profile = Profile();
        var ct = CancellationToken.None;
        try
        {
            await orchestrator.ApplyProfilesAsync([profile]);
            await orchestrator.GetPrinterFilesAsync(profile.PrinterId, ct);

            var first = await orchestrator.GetThumbnailAsync(profile.PrinterId, "a.gcode", ct);
            var second = await orchestrator.GetThumbnailAsync(profile.PrinterId, "a.gcode", ct);

            Assert.Equal(["list", "thumb:a.gcode"], sessions.Single().Calls);
            Assert.Equal(first!.Data, second!.Data);
        }
        finally
        {
            await orchestrator.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Operations_for_an_unknown_printer_do_nothing()
    {
        var (orchestrator, _, _, _) = Create();
        var unknown = Guid.NewGuid();
        var ct = CancellationToken.None;

        await orchestrator.PausePrintJobAsync(unknown, ct);

        Assert.Empty(await orchestrator.GetPrinterFilesAsync(unknown, ct));
        Assert.Null(await orchestrator.GetThumbnailAsync(unknown, "a.gcode", ct));
    }

    [Fact]
    public async Task Starting_on_a_printer_without_a_session_fails_instead_of_doing_nothing()
    {
        var (orchestrator, _, _, _) = Create();
        var unknown = Guid.NewGuid();
        var ct = CancellationToken.None;

        await Assert.ThrowsAsync<NotFoundException>(() => orchestrator.GetPrintStartPlanAsync(unknown, "a.gcode.3mf", ct));
        await Assert.ThrowsAsync<NotFoundException>(() => orchestrator.StartPrintJobAsync(unknown, "a.gcode.3mf", new PrintStartChoices(), ct));
        await Assert.ThrowsAsync<NotFoundException>(() => orchestrator.StartPrintJobAsync(unknown, "a.gcode.3mf", null, ct));
    }

    [Fact]
    public async Task A_connector_without_a_capability_is_skipped_not_failed()
    {
        var connector = new FakeConnector(Id, _ => new MinimalSession());
        var orchestrator = new PrinterOrchestrator(
            new ConnectorRegistry().Add(connector), new FakeTelemetry(),
            dataRoot: Path.Combine(Path.GetTempPath(), "octagrid-tests"));
        var profile = Profile();
        var ct = CancellationToken.None;
        await orchestrator.ApplyProfilesAsync([profile]);

        await orchestrator.DeleteFileAsync(profile.PrinterId, "a.gcode", ct);

        Assert.Empty(await orchestrator.GetPrinterFilesAsync(profile.PrinterId, ct));
        Assert.Null(await orchestrator.GetThumbnailAsync(profile.PrinterId, "a.gcode", ct));

        // A start the user confirmed must not vanish: it says the printer cannot print.
        await Assert.ThrowsAsync<ValidationException>(() => orchestrator.StartPrintJobAsync(profile.PrinterId, "a.gcode", null, ct));
        await Assert.ThrowsAsync<ValidationException>(() => orchestrator.GetPrintStartPlanAsync(profile.PrinterId, "a.gcode", ct));
        await orchestrator.DisposeAsync();
    }

    [Fact]
    public async Task Tells_clients_a_printer_is_offline_when_its_session_is_removed()
    {
        var (orchestrator, _, _, telemetry) = Create();
        var profile = Profile();
        await orchestrator.ApplyProfilesAsync([profile]);

        await orchestrator.ApplyProfilesAsync([]);

        var offline = telemetry.Sent[^1];
        Assert.Equal(profile.PrinterId, offline.PrinterId);
        Assert.Equal(OctaGrid.Extensions.Models.UniversalPrinterState.Offline, offline.State);
    }

    [Fact]
    public async Task Reports_connecting_when_a_session_starts_or_restarts_but_not_offline()
    {
        var (orchestrator, _, _, telemetry) = Create();
        var id = Guid.NewGuid();
        await orchestrator.ApplyProfilesAsync([Profile(id, url: "10.0.0.1")]);

        await orchestrator.ApplyProfilesAsync([Profile(id, url: "10.0.0.2")]);
        await orchestrator.DisposeAsync();

        Assert.Equal(2, telemetry.Sent.Count);
        Assert.All(telemetry.Sent, t =>
        {
            Assert.Equal(id, t.PrinterId);
            Assert.Equal(OctaGrid.Extensions.Models.UniversalPrinterState.Connecting, t.State);
        });
    }

    [Fact]
    public async Task Counts_the_active_sessions_per_connector()
    {
        var (orchestrator, _, _, _) = Create();
        await orchestrator.ApplyProfilesAsync([Profile(), Profile(), Profile(connectorId: "acme.missing")]);

        Assert.Equal(2, orchestrator.ActiveSessionCount(Id));
        Assert.Equal(2, orchestrator.ActiveSessionCount("ACME.FAKE"));
        Assert.Equal(0, orchestrator.ActiveSessionCount("acme.missing"));
        await orchestrator.DisposeAsync();
    }
}
