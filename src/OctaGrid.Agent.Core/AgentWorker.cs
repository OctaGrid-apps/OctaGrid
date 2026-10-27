using OctaGrid.Agent.Core.Clients.BambuMQTT;
using OctaGrid.Agent.Core.Clients.Moonraker;
using OctaGrid.Protocol.Agents;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Agent.Core;

public class AgentWorker : IAsyncDisposable
{
    private readonly List<IDeviceClientFactory> _factories = [new MoonrakerClientFactory(), new BambulabClientFactory()];
    private readonly DeviceOrchestrator _orchestrator;
    private readonly IPrinterProfileSource _profileSource;

    public AgentWorker(ITelemetryService telemetryService, IPrinterProfileSource profileSource)
    {
        _profileSource = profileSource;
        _orchestrator = new DeviceOrchestrator(_factories, telemetryService);
    }

    public Task DeleteFileAsync(Guid deviceId, string fileName, CancellationToken cancellationToken) =>
        _orchestrator.DeleteFileAsync(deviceId, fileName, cancellationToken);

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return _orchestrator.DisposeAsync();
    }

    public async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var profiles = await _profileSource.GetProfilesAsync(stoppingToken);
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
        var profiles = await _profileSource.GetProfilesAsync(cancellationToken);
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
        var factory = _factories.FirstOrDefault(f => f.CanHandle(protocol));
        if (factory is null)
        {
            return ConnectionTestResult.Fail($"Protocol '{protocol}' is not supported.");
        }

        var settings = string.IsNullOrEmpty(apiKey)
            ? []
            : new Dictionary<string, string> { ["ApiKey"] = apiKey };
        var profile = new DeviceProfile(Guid.NewGuid(), protocol, connectionString, settings);

        await using var client = factory.Create(profile);
        return await client.TestConnectionAsync(cancellationToken);
    }

    public Task TriggerInitialStateAsync(Guid deviceId, CancellationToken cancellationToken) =>
                        _orchestrator.TriggerInitialStateAsync(deviceId, cancellationToken);

    public Task TriggerInitialStateAsync(IEnumerable<Guid> devices, CancellationToken cancellationToken) =>
        _orchestrator.TriggerInitialStateAsync(devices, cancellationToken);
    public Task UploadFileAsync(Guid deviceId, Stream stream, string fileName, CancellationToken cancellationToken) =>
        _orchestrator.UploadFileAsync(deviceId, stream, fileName, cancellationToken);
}
