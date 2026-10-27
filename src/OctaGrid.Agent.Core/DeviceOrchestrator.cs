using System.Collections.Concurrent;
using OctaGrid.Protocol.Agents;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Agent.Core;

public class DeviceOrchestrator : IAsyncDisposable
{
    private sealed record DeviceSession(
        DeviceProfile Profile,
        IDeviceClient Client,
        CancellationTokenSource Cts,
        Task ExecutionTask
    );

    private readonly IEnumerable<IDeviceClientFactory> _factories;
    private readonly ConcurrentDictionary<Guid, DeviceSession> _sessions = new();
    private readonly ITelemetryService _telemetryService;

    public DeviceOrchestrator(IEnumerable<IDeviceClientFactory> factories, ITelemetryService telemetryService)
    {
        _factories = factories;
        _telemetryService = telemetryService;
    }

    public async Task ApplyProfilesAsync(IReadOnlyList<DeviceProfile> desiredProfiles)
    {
        var desiredMap = desiredProfiles.ToDictionary(p => p.DeviceId);
        var currentIds = _sessions.Keys.ToList();

        // 1. Remove deleted devices
        foreach (var id in currentIds)
        {
            if (!desiredMap.ContainsKey(id))
            {
                await StopDeviceSessionAsync(id);
            }
        }

        // 2. Add or Update changed devices
        foreach (var desired in desiredProfiles)
        {
            if (_sessions.TryGetValue(desired.DeviceId, out var session))
            {
                // Recreate worker if connection string or protocol changed
                if (session.Profile != desired)
                {
                    await StopDeviceSessionAsync(desired.DeviceId);
                    StartDeviceSession(desired);
                }
            }
            else
            {
                StartDeviceSession(desired);
            }
        }
    }

    public async Task DeleteFileAsync(Guid deviceId, string fileName, CancellationToken cancellationToken)
    {
        if (_sessions.TryGetValue(deviceId, out var session))
        {
            await session.Client.DeleteFileAsync(fileName, cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var id in _sessions.Keys)
        {
            await StopDeviceSessionAsync(id);
        }
        GC.SuppressFinalize(this);
    }

    public Task<IEnumerable<PrintMetadata>> GetPrinterFilesAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        if (_sessions.TryGetValue(deviceId, out var session))
        {
            return session.Client.GetPrinterFilesAsync(cancellationToken);
        }

        return Task.FromResult(Enumerable.Empty<PrintMetadata>());
    }

    public Task<PrintThumbnail?> GetThumbnailAsync(Guid deviceId, string fileName, CancellationToken cancellationToken) =>
        _sessions.TryGetValue(deviceId, out var session) && session.Client is IThumbnailProvider provider
            ? provider.GetThumbnailAsync(fileName, cancellationToken)
            : Task.FromResult<PrintThumbnail?>(null);

    public async Task PausePrintJobAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        if (_sessions.TryGetValue(deviceId, out var session))
        {
            await session.Client.PausePrintJobAsync(cancellationToken);
        }
    }

    public async Task ResumePrintJobAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        if (_sessions.TryGetValue(deviceId, out var session))
        {
            await session.Client.ResumePrintJobAsync(cancellationToken);
        }
    }

    public async Task StartPrintJobAsync(Guid deviceId, string fileName, CancellationToken cancellationToken)
    {
        if (_sessions.TryGetValue(deviceId, out var session))
        {
            await session.Client.StartPrintJobAsync(fileName, cancellationToken);
        }
    }

    public async Task StopPrintJobAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        if (_sessions.TryGetValue(deviceId, out var session))
        {
            await session.Client.StopPrintJobAsync(cancellationToken);
        }
    }

    public Task TriggerInitialStateAsync(Guid deviceId, CancellationToken cancellationToken) =>
                                        _sessions.TryGetValue(deviceId, out var session)
            ? session.Client.TriggerInitialStateAsync(cancellationToken)
            : Task.CompletedTask;

    public async Task TriggerInitialStateAsync(IEnumerable<Guid> devices, CancellationToken cancellationToken)
    {
        // ToDo: improve
        foreach (var deviceId in devices)
        {
            if (_sessions.TryGetValue(deviceId, out var session))
            {
                await session.Client.TriggerInitialStateAsync(cancellationToken);
            }
        }
    }

    public async Task UploadFileAsync(Guid deviceId, Stream stream, string fileName, CancellationToken cancellationToken)
    {
        if (_sessions.TryGetValue(deviceId, out var session))
        {
            await session.Client.UploadFileAsync(stream, fileName, cancellationToken);
        }
    }

    private void StartDeviceSession(DeviceProfile profile)
    {
        var factory = _factories.FirstOrDefault(f => f.CanHandle(profile.Protocol))
            ?? throw new NotSupportedException($"Protocol '{profile.Protocol}' is not supported.");

        var client = factory.Create(profile);
        var cts = new CancellationTokenSource();

        var task = Task.Run(async () =>
        {
            try
            {
                await client.RunAsync(_telemetryService, cts.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                // Log unhandled worker crash
                Console.WriteLine($"Fault in device worker {profile.DeviceId}: {ex.Message}");
            }
        });

        _sessions[profile.DeviceId] = new DeviceSession(profile, client, cts, task);
    }

    private async Task StopDeviceSessionAsync(Guid deviceId)
    {
        if (_sessions.TryRemove(deviceId, out var session))
        {
            session.Cts.Cancel();
            try
            {
                await session.ExecutionTask;
            }
            catch (Exception) { /* Ignored on cancel */ }

            await session.Client.DisposeAsync();
            session.Cts.Dispose();
        }
    }
}
