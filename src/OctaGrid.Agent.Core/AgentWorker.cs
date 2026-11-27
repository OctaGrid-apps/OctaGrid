using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Agent.Core.Connectors;
using OctaGrid.Agent.Core.Events;
using OctaGrid.Agent.Core.Extensions;
using OctaGrid.Agent.Core.Files;
using OctaGrid.Extensions.Abstractions;
using OctaGrid.Extensions.Models;
using OctaGrid.Protocol.AgentLink.Notifications;
using OctaGrid.Protocol.Extensions;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Agent.Core;

public class AgentWorker : IAsyncDisposable
{
    private readonly SemaphoreSlim _applyGate = new(1, 1);
    private readonly PrinterConsoleLog _console;
    private readonly PrinterEventLog _events;
    private readonly ExtensionManager _extensions;
    private readonly ILogger<AgentWorker> _logger;
    private readonly PrinterOrchestrator _orchestrator;
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private readonly PrinterTelemetryLog _telemetry;
    private DeveloperExtensionWatcher? _developerWatcher;
    private IReadOnlyList<PrinterProfile> _profiles = [];

    /// <param name="extensions">The installed extensions and which of them are enabled.</param>
    /// <param name="fileCache">Keeps what was read from printers' files across requests and restarts; optional.</param>
    public AgentWorker(
        ExtensionManager extensions,
        ILoggerFactory? loggerFactory = null,
        TimeProvider? timeProvider = null,
        PrinterFileCache? fileCache = null)
    {
        var factory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = factory.CreateLogger<AgentWorker>();
        _extensions = extensions;
        _events = new PrinterEventLog(timeProvider, logger: factory.CreateLogger<PrinterEventLog>());
        _console = new PrinterConsoleLog(timeProvider, logger: factory.CreateLogger<PrinterConsoleLog>());
        _telemetry = new PrinterTelemetryLog(timeProvider);
        _orchestrator = new PrinterOrchestrator(
            extensions.Registry,
            new EventRecordingTelemetrySink(new HistorySink(this), _events, factory.CreateLogger<EventRecordingTelemetrySink>()),
            factory,
            console: _console,
            fileCache: fileCache);
    }

    /// <summary>
    /// Raised for every snapshot a printer reports, with the history sample stored from it, so clients can show both
    /// live. Raised on the thread of the connector that reported it; a handler that throws does not affect the connector
    /// or the other handlers. Handlers must return quickly, because they delay that connector.
    /// </summary>
    public event Action<PrinterTelemetryReported>? TelemetryReported;

    /// <summary>
    /// Raised when the connectors this agent offers may have changed: an extension was turned on or off,
    /// approved, removed or reloaded. Read <see cref="ConnectorDescriptors"/> for the new set.
    /// </summary>
    public event Action? ConnectorsChanged;

    /// <summary>Raised when a printer writes a console line, so clients can show it live. See <see cref="PrinterConsoleLog.Recorded"/>.</summary>
    public event Action<PrinterConsoleLineRecorded>? ConsoleLineRecorded
    {
        add => _console.Recorded += value;
        remove => _console.Recorded -= value;
    }

    /// <summary>Raised when an event is recorded for a printer, so clients can show it live. See <see cref="PrinterEventLog.Recorded"/>.</summary>
    public event Action<PrinterEventRecorded>? EventRecorded
    {
        add => _events.Recorded += value;
        remove => _events.Recorded -= value;
    }

    /// <summary>Descriptors of the enabled connectors, for clients.</summary>
    public IReadOnlyList<ConnectorDescriptor> ConnectorDescriptors => _extensions.EnabledDescriptors;

    /// <summary>The folder extensions are loaded from in developer mode, or null when developer mode is off.</summary>
    public string? DeveloperFolder => _extensions.DeveloperFolder;

    /// <summary>
    /// Tells the agent which printers it runs. The backend sends the complete set when the agent starts and
    /// whenever a printer is added, changed or removed. The agent keeps it, so it can start the same printers
    /// again after an extension is turned on or reloaded.
    /// </summary>
    /// <param name="profiles">Every printer the agent has to run, with settings and secrets.</param>
    /// <param name="cancellationToken">Cancels waiting for an earlier change to finish.</param>
    public Task ApplyProfilesAsync(IReadOnlyList<PrinterProfile> profiles, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        return ApplyAsync(profiles.ToList(), cancellationToken);
    }

    /// <summary>Trusts an installed extension and loads it. Printers that were waiting for its connector start.</summary>
    public async Task<ExtensionInstallResult> ApproveExtensionAsync(string extensionId, CancellationToken cancellationToken)
    {
        var result = _extensions.Approve(extensionId);
        _logger.LogInformation("Approval of extension '{ExtensionId}': {Success} {Message}", extensionId, result.Success, result.Message);
        if (result.Success)
        {
            await ReapplyProfilesAsync(cancellationToken);
            ConnectorsChanged?.Invoke();
        }

        return result;
    }

    public Task DeleteFileAsync(Guid printerId, string fileName, CancellationToken cancellationToken) =>
            _orchestrator.DeleteFileAsync(printerId, fileName, cancellationToken);

    public ValueTask DisposeAsync()
    {
        _logger.LogInformation("Disposing AgentWorker and stopping all printer sessions");
        GC.SuppressFinalize(this);
        _developerWatcher?.Dispose();
        return _orchestrator.DisposeAsync();
    }

    /// <summary>All installed extensions with their current state.</summary>
    public IReadOnlyList<ExtensionInfo> GetExtensions() =>
        _extensions.GetExtensions()
            .Select(e => e with { ActivePrinters = _orchestrator.ActiveSessionCount(e.Id) })
            .ToList();

    /// <summary>
    /// What a printer wrote to its console, oldest first, for a client that needs to catch up. See
    /// <see cref="PrinterConsoleLog.Get"/> for the cursor rules.
    /// </summary>
    public ConsolePage GetPrinterConsole(Guid printerId, long? afterSequence, int limit) =>
        _console.Get(printerId, afterSequence, limit);

    /// <summary>
    /// What happened to a printer, oldest first, for a client that needs to catch up. See
    /// <see cref="PrinterEventLog.Get"/> for the cursor rules.
    /// </summary>
    public PrinterEventPage GetPrinterEvents(Guid printerId, long? afterSequence, int limit) =>
        _events.Get(printerId, afterSequence, limit);

    /// <summary>
    /// The last snapshot and recent history of a printer, for a client that starts showing it. See
    /// <see cref="PrinterTelemetryLog.Get"/>.
    /// </summary>
    public PrinterBackfill GetPrinterBackfill(Guid printerId, DateTimeOffset? after) => _telemetry.Get(printerId, after);

    public Task<IEnumerable<PrintMetadata>> GetPrinterFilesAsync(Guid printerId, CancellationToken cancellationToken) =>
            _orchestrator.GetPrinterFilesAsync(printerId, cancellationToken);

    public Task<IReadOnlyList<PrintMetadata>> GetPrinterFileDetailsAsync(
        Guid printerId, IReadOnlyList<string> fileNames, CancellationToken cancellationToken) =>
            _orchestrator.GetPrinterFileDetailsAsync(printerId, fileNames, cancellationToken);

    public Task<PrintThumbnail?> GetThumbnailAsync(Guid printerId, string fileName, CancellationToken cancellationToken) =>
            _orchestrator.GetThumbnailAsync(printerId, fileName, cancellationToken);

    /// <summary>Installs an extension package. Nothing from it runs until the user approves it.</summary>
    public ExtensionInstallResult InstallExtension(string packagePath)
    {
        var result = _extensions.Install(packagePath);
        _logger.LogInformation("Install of extension package {Package}: {Success} {Message}", packagePath, result.Success, result.Message);
        return result;
    }

    public Task PausePrintJobAsync(Guid printerId, CancellationToken cancellationToken) =>
            _orchestrator.PausePrintJobAsync(printerId, cancellationToken);

    /// <summary>
    /// Loads the developer folder again, for example after a build. The printers that use a developer extension
    /// restart their session with the new version.
    /// </summary>
    public async Task<ExtensionInstallResult> ReloadDeveloperExtensionsAsync(CancellationToken cancellationToken)
    {
        await _reloadGate.WaitAsync(cancellationToken);
        try
        {
            _extensions.BeginDeveloperReload();
            await ReapplyProfilesAsync(cancellationToken);    // stops the sessions that use the old version
            _extensions.CompleteDeveloperReload();
            await ReapplyProfilesAsync(cancellationToken);    // starts them again with the new one
            ConnectorsChanged?.Invoke();
            return ExtensionInstallResult.Ok(null, "Developer extensions reloaded.");
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    public Task ResumePrintJobAsync(Guid printerId, CancellationToken cancellationToken) =>
            _orchestrator.ResumePrintJobAsync(printerId, cancellationToken);

    /// <summary>
    /// Sends a command the user typed to a printer.
    /// See <see cref="PrinterOrchestrator.SendConsoleCommandAsync"/> for the checks and errors.
    /// </summary>
    public Task SendConsoleCommandAsync(Guid printerId, string command, CancellationToken cancellationToken) =>
        _orchestrator.SendConsoleCommandAsync(printerId, command, cancellationToken);

    /// <summary>
    /// Turns developer mode on for a folder (or off with null), loads what is in it and, while it is on, reloads
    /// whenever a build changes the folder.
    /// </summary>
    public async Task<ExtensionInstallResult> SetDeveloperFolderAsync(string? folder, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(folder) && !Directory.Exists(folder))
        {
            return ExtensionInstallResult.Fail("That folder does not exist.");
        }

        if (_extensions.DeveloperFolder is null && !string.IsNullOrWhiteSpace(folder))
        {
            ExtensionLoader.CleanUpShadowCopies();
        }

        _developerWatcher?.Dispose();
        _developerWatcher = null;
        _extensions.SetDeveloperFolder(folder);

        if (_extensions.DeveloperFolder is { } watched)
        {
            _developerWatcher = new DeveloperExtensionWatcher(watched, () => _ = ReloadOnChangeAsync());
        }

        await ReloadDeveloperExtensionsAsync(cancellationToken);
        _logger.LogInformation("Developer folder is now {Folder}", _extensions.DeveloperFolder ?? "(off)");
        return ExtensionInstallResult.Ok(null, _extensions.DeveloperFolder is null ? "Developer mode is off." : "Developer mode is on.");
    }

    /// <summary>
    /// Turns an extension on or off. Printers that use a disabled connector stop their session; they
    /// start again when it is enabled.
    /// </summary>
    public async Task SetExtensionEnabledAsync(string extensionId, bool enabled, CancellationToken cancellationToken)
    {
        if (!_extensions.SetEnabled(extensionId, enabled))
        {
            _logger.LogWarning("Cannot {Action} extension '{ExtensionId}': it is not installed",
                enabled ? "enable" : "disable", extensionId);
            return;
        }

        _logger.LogInformation("Extension '{ExtensionId}' {State}", extensionId, enabled ? "enabled" : "disabled");
        await ReapplyProfilesAsync(cancellationToken);
        ConnectorsChanged?.Invoke();
    }

    public Task StartPrintJobAsync(Guid printerId, string fileName, CancellationToken cancellationToken) =>
            _orchestrator.StartPrintJobAsync(printerId, fileName, cancellationToken);

    public Task StopPrintJobAsync(Guid printerId, CancellationToken cancellationToken) =>
            _orchestrator.StopPrintJobAsync(printerId, cancellationToken);

    /// <summary>Checks that a printer can be reached with the given settings, before it is saved.</summary>
    /// <param name="connectorId">The connector to test with.</param>
    /// <param name="settings">The settings entered for the printer, including secrets, by the keys the connector declared.</param>
    /// <param name="cancellationToken">Cancels the test.</param>
    public async Task<ConnectionTestResult> TestConnectionAsync(
        string connectorId, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken)
    {
        // Settings can hold secrets, so only the connector id is logged.
        _logger.LogInformation("Testing printer connection with connector '{ConnectorId}'", connectorId);

        if (_extensions.IsDisabled(connectorId))
        {
            _logger.LogWarning("Connection test failed: connector '{ConnectorId}' is disabled", connectorId);
            return ConnectionTestResult.Fail($"Connector '{connectorId}' is disabled. Enable it under Settings > Extensions.");
        }

        if (!_extensions.TryGetConnector(connectorId, out var connector))
        {
            _logger.LogWarning("Connection test failed: connector '{ConnectorId}' is not available", connectorId);
            return ConnectionTestResult.Fail($"Connector '{connectorId}' is not available.");
        }

        var profile = new PrinterProfile(
            Guid.NewGuid(),
            connector.Descriptor.Id,
            settings.Where(kv => !string.IsNullOrEmpty(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value));
        var context = new ConnectorContext(
            _logger, Path.Combine(ConnectorContext.DefaultRoot, connector.Descriptor.Id, "connection-test"));

        await using var session = connector.CreateSession(profile, context);
        var result = await session.TestConnectionAsync(cancellationToken);
        if (result.Success)
        {
            _logger.LogInformation("Connection test succeeded for connector '{ConnectorId}'", connectorId);
        }
        else
        {
            _logger.LogWarning("Connection test failed for connector '{ConnectorId}': {Error}", connectorId, result.Error);
        }

        return result;
    }

    /// <summary>Removes an installed extension. Printers that use it stop their session.</summary>
    public async Task<ExtensionInstallResult> UninstallExtensionAsync(string extensionId, CancellationToken cancellationToken)
    {
        var result = _extensions.Uninstall(extensionId);
        _logger.LogInformation("Removal of extension '{ExtensionId}': {Success} {Message}", extensionId, result.Success, result.Message);
        if (result.Success)
        {
            await ReapplyProfilesAsync(cancellationToken);
            ConnectorsChanged?.Invoke();
        }

        return result;
    }

    public Task UploadFileAsync(Guid printerId, Stream stream, string fileName, CancellationToken cancellationToken) =>
        _orchestrator.UploadFileAsync(printerId, stream, fileName, cancellationToken);

    // One at a time, and the set is replaced and read inside the gate: otherwise a re-apply that read the old set
    // could finish last, stop the session of a printer added in between, and forget its history.
    private async Task ApplyAsync(IReadOnlyList<PrinterProfile>? replacement, CancellationToken cancellationToken)
    {
        await _applyGate.WaitAsync(cancellationToken);
        try
        {
            if (replacement is not null)
            {
                _profiles = replacement;
            }

            var profiles = _profiles;
            // Profiles carry secrets, so only their number is logged.
            _logger.LogInformation("Applying {ProfileCount} printer profile(s)", profiles.Count);
            await _orchestrator.ApplyProfilesAsync(WithoutDisabledConnectors(profiles));

            // A printer that was deleted takes its history with it; one whose connector is only turned off keeps it.
            var printerIds = profiles.Select(p => p.PrinterId).ToList();
            _events.Retain(printerIds);
            _console.Retain(printerIds);
            _telemetry.Retain(printerIds);
        }
        finally
        {
            _applyGate.Release();
        }
    }

    // Applies the printers the agent already has again, after the set of usable connectors changed.
    private Task ReapplyProfilesAsync(CancellationToken cancellationToken) => ApplyAsync(null, cancellationToken);

    private async Task ReloadOnChangeAsync()
    {
        try
        {
            _logger.LogInformation("The developer folder changed; reloading developer extensions");
            await ReloadDeveloperExtensionsAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload developer extensions");
        }
    }

    // A printer whose connector the user disabled gets no session; unknown connectors are passed
    // through so the orchestrator can report them.
    private List<PrinterProfile> WithoutDisabledConnectors(IReadOnlyList<PrinterProfile> profiles) =>
        profiles.Where(p => !_extensions.IsDisabled(p.ConnectorId)).ToList();

    private void OnTelemetry(PrinterTelemetry telemetry)
    {
        // The history is a side feature: if recording fails, the live state must still reach the clients.
        TelemetrySample? sample = null;
        try
        {
            sample = _telemetry.Record(telemetry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not record the telemetry history of printer {PrinterId}", telemetry.PrinterId);
        }

        if (TelemetryReported is not { } handlers)
        {
            return;
        }

        var reported = new PrinterTelemetryReported(telemetry, sample);
        foreach (var handler in handlers.GetInvocationList().Cast<Action<PrinterTelemetryReported>>())
        {
            try
            {
                handler(reported);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "A handler of telemetry for printer {PrinterId} failed", telemetry.PrinterId);
            }
        }
    }

    /// <summary>The end of the sink chain the printer sessions write to: records history, then raises the event.</summary>
    private sealed class HistorySink : ITelemetrySink
    {
        private readonly AgentWorker _worker;

        public HistorySink(AgentWorker worker)
        {
            _worker = worker;
        }

        public void Send(PrinterTelemetry telemetry) => _worker.OnTelemetry(telemetry);
    }
}
