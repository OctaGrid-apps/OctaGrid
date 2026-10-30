using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly ILogger<DeviceOrchestrator> _logger;
    private readonly ConcurrentDictionary<Guid, DeviceSession> _sessions = new();
    private readonly ITelemetryService _telemetryService;

    public DeviceOrchestrator(
        IEnumerable<IDeviceClientFactory> factories,
        ITelemetryService telemetryService,
        ILogger<DeviceOrchestrator>? logger = null)
    {
        _factories = factories;
        _telemetryService = telemetryService;
        _logger = logger ?? NullLogger<DeviceOrchestrator>.Instance;
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
                _logger.LogInformation("Removing session for deleted or disabled device {DeviceId}", id);
                await StopDeviceSessionAsync(id);
            }
        }

        // 2. Add or Update changed devices
        foreach (var desired in desiredProfiles)
        {
            if (_sessions.TryGetValue(desired.DeviceId, out var session))
            {
                // Recreate worker if connection string, protocol, or settings changed
                if (session.Profile != desired)
                {
                    _logger.LogInformation("Restarting session for device {DeviceId} due to profile change ({Protocol} @ {Endpoint})",
                        desired.DeviceId, desired.Protocol, desired.ConnectionString);
                    await StopDeviceSessionAsync(desired.DeviceId);
                    StartDeviceSession(desired);
                }
            }
            else
            {
                StartDeviceSession(desired);
            }
        }

        _logger.LogDebug("DeviceOrchestrator active session count: {ActiveSessions}", _sessions.Count);
    }

    public async Task DeleteFileAsync(Guid deviceId, string fileName, CancellationToken cancellationToken)
    {
        if (_sessions.TryGetValue(deviceId, out var session))
        {
            await session.Client.DeleteFileAsync(fileName, cancellationToken);
        }
        else
        {
            _logger.LogWarning("Cannot delete file '{FileName}': no active session for device {DeviceId}", fileName, deviceId);
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

        _logger.LogWarning("Cannot get printer files: no active session for device {DeviceId}", deviceId);
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
        else
        {
            _logger.LogWarning("Cannot pause print job: no active session for device {DeviceId}", deviceId);
        }
    }

    public async Task ResumePrintJobAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        if (_sessions.TryGetValue(deviceId, out var session))
        {
            await session.Client.ResumePrintJobAsync(cancellationToken);
        }
        else
        {
            _logger.LogWarning("Cannot resume print job: no active session for device {DeviceId}", deviceId);
        }
    }

    public async Task StartPrintJobAsync(Guid deviceId, string fileName, CancellationToken cancellationToken)
    {
        if (_sessions.TryGetValue(deviceId, out var session))
        {
            await session.Client.StartPrintJobAsync(fileName, cancellationToken);
        }
        else
        {
            _logger.LogWarning("Cannot start print job '{FileName}': no active session for device {DeviceId}", fileName, deviceId);
        }
    }

    public async Task StopPrintJobAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        if (_sessions.TryGetValue(deviceId, out var session))
        {
            await session.Client.StopPrintJobAsync(cancellationToken);
        }
        else
        {
            _logger.LogWarning("Cannot stop print job: no active session for device {DeviceId}", deviceId);
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
        else
        {
            _logger.LogWarning("Cannot upload file '{FileName}': no active session for device {DeviceId}", fileName, deviceId);
        }
    }

    private void StartDeviceSession(DeviceProfile profile)
    {
        var factory = _factories.FirstOrDefault(f => f.CanHandle(profile.Protocol))
            ?? throw new NotSupportedException($"Protocol '{profile.Protocol}' is not supported.");

        _logger.LogInformation("Starting session for device {DeviceId} ({Protocol} @ {Endpoint})",
            profile.DeviceId, profile.Protocol, profile.ConnectionString);

        var client = factory.Create(profile);
        var cts = new CancellationTokenSource();

        var task = Task.Run(async () =>
        {
            try
            {
                await client.RunAsync(_telemetryService, cts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Session loop cancelled for device {DeviceId}", profile.DeviceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled fault in device worker for device {DeviceId} ({Protocol} @ {Endpoint})",
                    profile.DeviceId, profile.Protocol, profile.ConnectionString);
            }
        });

        _sessions[profile.DeviceId] = new DeviceSession(profile, client, cts, task);
    }

    private async Task StopDeviceSessionAsync(Guid deviceId)
    {
        if (_sessions.TryRemove(deviceId, out var session))
        {
            _logger.LogDebug("Stopping session for device {DeviceId}", deviceId);
            session.Cts.Cancel();
            try
            {
                await session.ExecutionTask;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Device session {DeviceId} faulted while stopping", deviceId);
            }
            catch (OperationCanceledException)
            {
                // Ignored on cancel
            }

            await session.Client.DisposeAsync();
            session.Cts.Dispose();
            _logger.LogInformation("Stopped session for device {DeviceId}", deviceId);
        }
    }
}
