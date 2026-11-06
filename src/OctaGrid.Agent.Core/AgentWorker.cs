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
    private readonly ILogger<AgentWorker> _logger;
    private readonly DeviceOrchestrator _orchestrator;
    private readonly IPrinterProfileSource _profileSource;
    private readonly ConnectorRegistry _registry;

    /// <param name="registry">The connectors this agent can use.</param>
    public AgentWorker(
        ITelemetrySink telemetry,
        IPrinterProfileSource profileSource,
        ConnectorRegistry registry,
        ILoggerFactory? loggerFactory = null)
    {
        var factory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = factory.CreateLogger<AgentWorker>();
        _profileSource = profileSource;
        _registry = registry;
        _orchestrator = new DeviceOrchestrator(registry, telemetry, factory);
    }

    /// <summary>Descriptors of the available connectors, for clients.</summary>
    public IReadOnlyList<ConnectorDescriptor> ConnectorDescriptors => _registry.Descriptors;

    public Task DeleteFileAsync(Guid deviceId, string fileName, CancellationToken cancellationToken) =>
        _orchestrator.DeleteFileAsync(deviceId, fileName, cancellationToken);

    public ValueTask DisposeAsync()
    {
        _logger.LogInformation("Disposing AgentWorker and stopping all device sessions");
        GC.SuppressFinalize(this);
        return _orchestrator.DisposeAsync();
    }

    public async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AgentWorker starting initial profile load");
        var profiles = await _profileSource.GetProfilesAsync(stoppingToken);
        _logger.LogInformation("Loaded {ProfileCount} printer profile(s) from source", profiles.Count);
        await _orchestrator.ApplyProfilesAsync(profiles);
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
        await _orchestrator.ApplyProfilesAsync(profiles);
    }

    public Task ResumePrintJobAsync(Guid deviceId, CancellationToken cancellationToken) =>
        _orchestrator.ResumePrintJobAsync(deviceId, cancellationToken);

    public Task StartPrintJobAsync(Guid deviceId, string fileName, CancellationToken cancellationToken) =>
        _orchestrator.StartPrintJobAsync(deviceId, fileName, cancellationToken);

    public Task StopPrintJobAsync(Guid deviceId, CancellationToken cancellationToken) =>
        _orchestrator.StopPrintJobAsync(deviceId, cancellationToken);

    /// <summary>Checks that a printer can be reached with the given settings, before it is saved.</summary>
    public async Task<ConnectionTestResult> TestConnectionAsync(string connectorId, string url, string? apiKey, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Testing printer connection with connector '{ConnectorId}' at '{Endpoint}'", connectorId, url);

        if (!_registry.TryGet(connectorId, out var connector))
        {
            _logger.LogWarning("Connection test failed: connector '{ConnectorId}' is not available", connectorId);
            return ConnectionTestResult.Fail($"Connector '{connectorId}' is not available.");
        }

        var settings = new Dictionary<string, string> { ["url"] = url };
        if (!string.IsNullOrEmpty(apiKey))
        {
            settings["apiKey"] = apiKey;
        }

        var profile = new PrinterProfile(Guid.NewGuid(), connector.Descriptor.Id, settings);
        var context = new ConnectorContext(
            _logger, Path.Combine(ConnectorContext.DefaultRoot, connector.Descriptor.Id, "connection-test"));

        await using var session = connector.CreateSession(profile, context);
        var result = await session.TestConnectionAsync(cancellationToken);
        if (result.Success)
        {
            _logger.LogInformation("Connection test succeeded for connector '{ConnectorId}' at '{Endpoint}'", connectorId, url);
        }
        else
        {
            _logger.LogWarning("Connection test failed for connector '{ConnectorId}' at '{Endpoint}': {Error}", connectorId, url, result.Error);
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
