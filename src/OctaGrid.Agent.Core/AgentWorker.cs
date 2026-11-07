using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Agent.Core.Connectors;
using OctaGrid.Extensions.Abstractions;
using OctaGrid.Protocol.Agents;
using OctaGrid.Protocol.Extensions;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Agent.Core;

public class AgentWorker : IAsyncDisposable
{
    private readonly ExtensionManager _extensions;
    private readonly ILogger<AgentWorker> _logger;
    private readonly DeviceOrchestrator _orchestrator;
    private readonly IPrinterProfileSource _profileSource;
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private DeveloperExtensionWatcher? _developerWatcher;

    /// <param name="extensions">The installed extensions and which of them are enabled.</param>
    public AgentWorker(
        ITelemetrySink telemetry,
        IPrinterProfileSource profileSource,
        ExtensionManager extensions,
        ILoggerFactory? loggerFactory = null)
    {
        var factory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = factory.CreateLogger<AgentWorker>();
        _profileSource = profileSource;
        _extensions = extensions;
        _orchestrator = new DeviceOrchestrator(extensions.Registry, telemetry, factory);
    }

    /// <summary>Descriptors of the enabled connectors, for clients.</summary>
    public IReadOnlyList<ConnectorDescriptor> ConnectorDescriptors => _extensions.EnabledDescriptors;

    /// <summary>All installed extensions with their current state.</summary>
    public IReadOnlyList<ExtensionInfo> GetExtensions() =>
        _extensions.GetExtensions()
            .Select(e => e with { ActivePrinters = _orchestrator.ActiveSessionCount(e.Id) })
            .ToList();

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
        await RefreshProfilesAsync(cancellationToken);
    }

    /// <summary>Installs an extension package. Nothing from it runs until the user approves it.</summary>
    public ExtensionInstallResult InstallExtension(string packagePath)
    {
        var result = _extensions.Install(packagePath);
        _logger.LogInformation("Install of extension package {Package}: {Success} {Message}", packagePath, result.Success, result.Message);
        return result;
    }

    /// <summary>Trusts an installed extension and loads it. Printers that were waiting for its connector start.</summary>
    public async Task<ExtensionInstallResult> ApproveExtensionAsync(string extensionId, CancellationToken cancellationToken)
    {
        var result = _extensions.Approve(extensionId);
        _logger.LogInformation("Approval of extension '{ExtensionId}': {Success} {Message}", extensionId, result.Success, result.Message);
        if (result.Success)
        {
            await RefreshProfilesAsync(cancellationToken);
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
            await RefreshProfilesAsync(cancellationToken);
        }

        return result;
    }

    /// <summary>The folder extensions are loaded from in developer mode, or null when developer mode is off.</summary>
    public string? DeveloperFolder => _extensions.DeveloperFolder;

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
    /// Loads the developer folder again, for example after a build. The printers that use a developer extension
    /// restart their session with the new version.
    /// </summary>
    public async Task<ExtensionInstallResult> ReloadDeveloperExtensionsAsync(CancellationToken cancellationToken)
    {
        await _reloadGate.WaitAsync(cancellationToken);
        try
        {
            _extensions.BeginDeveloperReload();
            await RefreshProfilesAsync(cancellationToken);    // stops the sessions that use the old version
            _extensions.CompleteDeveloperReload();
            await RefreshProfilesAsync(cancellationToken);    // starts them again with the new one
            return ExtensionInstallResult.Ok(null, "Developer extensions reloaded.");
        }
        finally
        {
            _reloadGate.Release();
        }
    }

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

    public Task DeleteFileAsync(Guid deviceId, string fileName, CancellationToken cancellationToken) =>
        _orchestrator.DeleteFileAsync(deviceId, fileName, cancellationToken);

    public ValueTask DisposeAsync()
    {
        _logger.LogInformation("Disposing AgentWorker and stopping all device sessions");
        GC.SuppressFinalize(this);
        _developerWatcher?.Dispose();
        return _orchestrator.DisposeAsync();
    }

    public async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AgentWorker starting initial profile load");
        var profiles = await _profileSource.GetProfilesAsync(stoppingToken);
        _logger.LogInformation("Loaded {ProfileCount} printer profile(s) from source", profiles.Count);
        await _orchestrator.ApplyProfilesAsync(WithoutDisabledConnectors(profiles));
    }

    public Task<IEnumerable<PrintMetadata>> GetPrinterFilesAsync(Guid deviceId, CancellationToken cancellationToken) =>
        _orchestrator.GetPrinterFilesAsync(deviceId, cancellationToken);

    public Task<PrintThumbnail?> GetThumbnailAsync(Guid deviceId, string fileName, CancellationToken cancellationToken) =>
        _orchestrator.GetThumbnailAsync(deviceId, fileName, cancellationToken);

    public Task PausePrintJobAsync(Guid deviceId, CancellationToken cancellationToken) =>
        _orchestrator.PausePrintJobAsync(deviceId, cancellationToken);

    public async Task RefreshProfilesAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Refreshing printer profiles from source");
        var profiles = await _profileSource.GetProfilesAsync(cancellationToken);
        _logger.LogInformation("Applying {ProfileCount} refreshed printer profile(s)", profiles.Count);
        await _orchestrator.ApplyProfilesAsync(WithoutDisabledConnectors(profiles));
    }

    public Task ResumePrintJobAsync(Guid deviceId, CancellationToken cancellationToken) =>
        _orchestrator.ResumePrintJobAsync(deviceId, cancellationToken);

    public Task StartPrintJobAsync(Guid deviceId, string fileName, CancellationToken cancellationToken) =>
        _orchestrator.StartPrintJobAsync(deviceId, fileName, cancellationToken);

    public Task StopPrintJobAsync(Guid deviceId, CancellationToken cancellationToken) =>
        _orchestrator.StopPrintJobAsync(deviceId, cancellationToken);

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

    public Task TriggerInitialStateAsync(Guid deviceId, CancellationToken cancellationToken) =>
        _orchestrator.TriggerInitialStateAsync(deviceId, cancellationToken);

    public Task TriggerInitialStateAsync(IEnumerable<Guid> devices, CancellationToken cancellationToken) =>
        _orchestrator.TriggerInitialStateAsync(devices, cancellationToken);

    public Task UploadFileAsync(Guid deviceId, Stream stream, string fileName, CancellationToken cancellationToken) =>
        _orchestrator.UploadFileAsync(deviceId, stream, fileName, cancellationToken);
}
