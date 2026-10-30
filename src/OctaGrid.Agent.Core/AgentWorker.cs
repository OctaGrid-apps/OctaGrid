using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Agent.Core.Clients.BambuMQTT;
using OctaGrid.Agent.Core.Clients.Moonraker;
using OctaGrid.Protocol.Agents;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Agent.Core;

public class AgentWorker : IAsyncDisposable
{
    private readonly List<IDeviceClientFactory> _factories;
    private readonly ILogger<AgentWorker> _logger;
    private readonly DeviceOrchestrator _orchestrator;
    private readonly IPrinterProfileSource _profileSource;

    public AgentWorker(
        ITelemetryService telemetryService,
        IPrinterProfileSource profileSource,
        ILoggerFactory? loggerFactory = null)
    {
        var factory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = factory.CreateLogger<AgentWorker>();
        _profileSource = profileSource;
        _factories =
        [
            new MoonrakerClientFactory(factory),
            new BambulabClientFactory(factory)
        ];
        _orchestrator = new DeviceOrchestrator(_factories, telemetryService, factory.CreateLogger<DeviceOrchestrator>());
    }

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

    public async Task<ConnectionTestResult> TestConnectionAsync(string protocol, string connectionString, string? apiKey, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Testing printer connection for protocol '{Protocol}' at '{Endpoint}'", protocol, connectionString);

        var factory = _factories.FirstOrDefault(f => f.CanHandle(protocol));
        if (factory is null)
        {
            _logger.LogWarning("Connection test failed: Protocol '{Protocol}' is not supported", protocol);
            return ConnectionTestResult.Fail($"Protocol '{protocol}' is not supported.");
        }

        var settings = string.IsNullOrEmpty(apiKey)
            ? []
            : new Dictionary<string, string> { ["ApiKey"] = apiKey };
        var profile = new DeviceProfile(Guid.NewGuid(), protocol, connectionString, settings);

        await using var client = factory.Create(profile);
        var result = await client.TestConnectionAsync(cancellationToken);
        if (result.Success)
        {
            _logger.LogInformation("Connection test succeeded for protocol '{Protocol}' at '{Endpoint}'", protocol, connectionString);
        }
        else
        {
            _logger.LogWarning("Connection test failed for protocol '{Protocol}' at '{Endpoint}': {Error}", protocol, connectionString, result.Error);
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
