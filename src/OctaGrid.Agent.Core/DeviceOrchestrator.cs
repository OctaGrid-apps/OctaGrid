using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Agent.Core.Connectors;
using OctaGrid.Extensions.Abstractions;
using OctaGrid.Protocol.Extensions;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Agent.Core;

/// <summary>
/// Keeps one live <see cref="IPrinterSession"/> per enabled printer, created by the printer's connector,
/// and routes commands to the capabilities each session supports.
/// </summary>
public class DeviceOrchestrator : IAsyncDisposable
{
    private sealed record DeviceSession(
        PrinterProfile Profile,
        IPrinterSession Session,
        CancellationTokenSource Cts,
        Task ExecutionTask
    );

    private readonly string _dataRoot;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<DeviceOrchestrator> _logger;
    private readonly ConnectorRegistry _registry;
    private readonly ConcurrentDictionary<Guid, DeviceSession> _sessions = new();
    private readonly ITelemetrySink _telemetry;

    public DeviceOrchestrator(
        ConnectorRegistry registry,
        ITelemetrySink telemetry,
        ILoggerFactory? loggerFactory = null,
        string? dataRoot = null)
    {
        _registry = registry;
        _telemetry = telemetry;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<DeviceOrchestrator>();
        _dataRoot = dataRoot ?? ConnectorContext.DefaultRoot;
    }

    public async Task ApplyProfilesAsync(IReadOnlyList<PrinterProfile> desiredProfiles)
    {
        var desiredMap = desiredProfiles.ToDictionary(p => p.PrinterId);
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

        // 2. Add or update changed devices
        foreach (var desired in desiredProfiles)
        {
            if (_sessions.TryGetValue(desired.PrinterId, out var session))
            {
                // Recreate the session if the connector or its settings changed
                if (!ProfilesEqual(session.Profile, desired))
                {
                    _logger.LogInformation("Restarting session for device {DeviceId} due to profile change ({ConnectorId})",
                        desired.PrinterId, desired.ConnectorId);
                    await StopDeviceSessionAsync(desired.PrinterId);
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
        if (TryGetCapability<IFileStorage>(deviceId, $"delete file '{fileName}'", out var files))
        {
            await files.DeleteFileAsync(fileName, cancellationToken);
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

    public async Task<IEnumerable<PrintMetadata>> GetPrinterFilesAsync(Guid deviceId, CancellationToken cancellationToken) =>
        TryGetCapability<IFileStorage>(deviceId, "get printer files", out var files)
            ? await files.ListFilesAsync(cancellationToken)
            : [];

    public Task<PrintThumbnail?> GetThumbnailAsync(Guid deviceId, string fileName, CancellationToken cancellationToken) =>
        _sessions.TryGetValue(deviceId, out var session) && session.Session is IThumbnails thumbnails
            ? thumbnails.GetThumbnailAsync(fileName, cancellationToken)
            : Task.FromResult<PrintThumbnail?>(null);

    public async Task PausePrintJobAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        if (TryGetCapability<IPrintControl>(deviceId, "pause print job", out var control))
        {
            await control.PausePrintAsync(cancellationToken);
        }
    }

    public async Task ResumePrintJobAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        if (TryGetCapability<IPrintControl>(deviceId, "resume print job", out var control))
        {
            await control.ResumePrintAsync(cancellationToken);
        }
    }

    public async Task StartPrintJobAsync(Guid deviceId, string fileName, CancellationToken cancellationToken)
    {
        if (TryGetCapability<IPrintControl>(deviceId, $"start print job '{fileName}'", out var control))
        {
            await control.StartPrintAsync(fileName, cancellationToken);
        }
    }

    public async Task StopPrintJobAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        if (TryGetCapability<IPrintControl>(deviceId, "stop print job", out var control))
        {
            await control.StopPrintAsync(cancellationToken);
        }
    }

    public Task TriggerInitialStateAsync(Guid deviceId, CancellationToken cancellationToken) =>
        _sessions.TryGetValue(deviceId, out var session)
            ? session.Session.RequestStateAsync(cancellationToken)
            : Task.CompletedTask;

    public async Task TriggerInitialStateAsync(IEnumerable<Guid> devices, CancellationToken cancellationToken)
    {
        // ToDo: improve
        foreach (var deviceId in devices)
        {
            if (_sessions.TryGetValue(deviceId, out var session))
            {
                await session.Session.RequestStateAsync(cancellationToken);
            }
        }
    }

    public async Task UploadFileAsync(Guid deviceId, Stream stream, string fileName, CancellationToken cancellationToken)
    {
        if (TryGetCapability<IFileStorage>(deviceId, $"upload file '{fileName}'", out var files))
        {
            await files.UploadFileAsync(stream, fileName, cancellationToken);
        }
    }

    /// <summary>
    /// Two profiles are the same when they would produce the same session. Settings are compared by
    /// content: profiles are rebuilt from the database on every refresh, so reference equality would
    /// restart every session each time.
    /// </summary>
    private static bool ProfilesEqual(PrinterProfile a, PrinterProfile b) =>
        a.PrinterId == b.PrinterId
        && string.Equals(a.ConnectorId, b.ConnectorId, StringComparison.OrdinalIgnoreCase)
        && a.Settings.Count == b.Settings.Count
        && a.Settings.All(kv => b.Settings.TryGetValue(kv.Key, out var other) && other == kv.Value);

    private bool TryGetCapability<T>(Guid deviceId, string operation, out T capability)
        where T : class
    {
        if (!_sessions.TryGetValue(deviceId, out var session))
        {
            _logger.LogWarning("Cannot {Operation}: no active session for device {DeviceId}", operation, deviceId);
            capability = null!;
            return false;
        }

        if (session.Session is not T supported)
        {
            _logger.LogWarning("Cannot {Operation}: the connector for device {DeviceId} does not support it", operation, deviceId);
            capability = null!;
            return false;
        }

        capability = supported;
        return true;
    }

    private void StartDeviceSession(PrinterProfile profile)
    {
        // A printer whose connector is missing must not stop the other printers from starting.
        if (!_registry.TryGet(profile.ConnectorId, out var connector))
        {
            _logger.LogError("Cannot start device {DeviceId}: connector '{ConnectorId}' is not available",
                profile.PrinterId, profile.ConnectorId);
            return;
        }

        _logger.LogInformation("Starting session for device {DeviceId} ({ConnectorId})",
            profile.PrinterId, connector.Descriptor.Id);

        var context = new ConnectorContext(
            _loggerFactory.CreateLogger($"Connector.{connector.Descriptor.Id}"),
            Path.Combine(_dataRoot, connector.Descriptor.Id, profile.PrinterId.ToString("N")));

        var session = connector.CreateSession(profile, context);
        var cts = new CancellationTokenSource();

        var task = Task.Run(async () =>
        {
            try
            {
                await session.RunAsync(_telemetry, cts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Session loop cancelled for device {DeviceId}", profile.PrinterId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled fault in device worker for device {DeviceId} ({ConnectorId})",
                    profile.PrinterId, profile.ConnectorId);
            }
        });

        _sessions[profile.PrinterId] = new DeviceSession(profile, session, cts, task);
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

            await session.Session.DisposeAsync();
            session.Cts.Dispose();
            _logger.LogInformation("Stopped session for device {DeviceId}", deviceId);
        }
    }
}
