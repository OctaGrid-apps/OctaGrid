using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Agent.Core.Connectors;
using OctaGrid.Agent.Core.Events;
using OctaGrid.Agent.Core.Files;
using OctaGrid.Extensions.Abstractions;
using OctaGrid.Extensions.Models;
using OctaGrid.Extensions.Models.Exceptions;

namespace OctaGrid.Agent.Core;

/// <summary>
/// Keeps one live <see cref="IPrinterSession"/> per enabled printer, created by the printer's connector,
/// and routes commands to the capabilities each session supports.
/// </summary>
public class PrinterOrchestrator : IAsyncDisposable
{
    private sealed record RunningPrinter(
        PrinterProfile Profile,
        IPrinterSession Session,
        CancellationTokenSource Cts,
        Task ExecutionTask
    );

    private readonly PrinterConsoleLog? _console;
    private readonly string _dataRoot;
    private readonly PrinterFileCache? _fileCache;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<PrinterOrchestrator> _logger;
    private readonly ConnectorRegistry _registry;
    private readonly ConcurrentDictionary<Guid, RunningPrinter> _sessions = new();
    private readonly TimeSpan _stopTimeout;
    private readonly ITelemetrySink _telemetry;

    /// <param name="stopTimeout">How long to wait for a session to end after it is told to stop. Defaults to ten seconds.</param>
    /// <param name="console">Where the console output of sessions that report it is kept. Without it, that output is dropped.</param>
    /// <param name="fileCache">
    /// Caches file details and thumbnails. Without it, every request reads from the printer.
    /// </param>
    public PrinterOrchestrator(
        ConnectorRegistry registry,
        ITelemetrySink telemetry,
        ILoggerFactory? loggerFactory = null,
        string? dataRoot = null,
        TimeSpan? stopTimeout = null,
        PrinterConsoleLog? console = null,
        PrinterFileCache? fileCache = null)
    {
        _console = console;
        _fileCache = fileCache;
        _stopTimeout = stopTimeout ?? TimeSpan.FromSeconds(10);
        _registry = registry;
        _telemetry = telemetry;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<PrinterOrchestrator>();
        _dataRoot = dataRoot ?? ConnectorContext.DefaultRoot;
    }

    public async Task ApplyProfilesAsync(IReadOnlyList<PrinterProfile> desiredProfiles)
    {
        var desiredMap = desiredProfiles.ToDictionary(p => p.PrinterId);
        var currentIds = _sessions.Keys.ToList();

        // 1. Remove deleted printers
        foreach (var id in currentIds)
        {
            if (!desiredMap.ContainsKey(id))
            {
                _logger.LogInformation("Removing session for deleted or disabled printer {PrinterId}", id);
                await StopSessionAsync(id);

                // Nothing reports for this printer any more, so tell clients it is offline instead of
                // leaving its last state on screen. (A restart or shutdown does not send this.)
                _telemetry.Send(new PrinterTelemetry(id));
            }
        }

        // 2. Add or update changed printers
        foreach (var desired in desiredProfiles)
        {
            if (_sessions.TryGetValue(desired.PrinterId, out var session))
            {
                // Recreate the session if the connector or its settings changed
                if (!ProfilesEqual(session.Profile, desired))
                {
                    _logger.LogInformation("Restarting session for printer {PrinterId} due to profile change ({ConnectorId})",
                        desired.PrinterId, desired.ConnectorId);
                    await StopSessionAsync(desired.PrinterId);
                    StartSession(desired);
                }
            }
            else
            {
                StartSession(desired);
            }
        }

        _logger.LogDebug("PrinterOrchestrator active session count: {ActiveSessions}", _sessions.Count);
    }

    /// <summary>How many printers currently have a session created by the given connector.</summary>
    public int ActiveSessionCount(string connectorId) =>
        _sessions.Values.Count(s => string.Equals(s.Profile.ConnectorId, connectorId, StringComparison.OrdinalIgnoreCase));

    public async Task DeleteFileAsync(Guid printerId, string fileName, CancellationToken cancellationToken)
    {
        if (TryGetCapability<IFileStorage>(printerId, $"delete file '{fileName}'", out var files))
        {
            await files.DeleteFileAsync(fileName, cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var id in _sessions.Keys)
        {
            await StopSessionAsync(id);
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>The printer's files, with details from the file cache filled in.</summary>
    public async Task<IEnumerable<PrintMetadata>> GetPrinterFilesAsync(Guid printerId, CancellationToken cancellationToken)
    {
        if (!TryGetCapability<IFileStorage>(printerId, "get printer files", out var files))
        {
            return [];
        }

        var listing = await files.ListFilesAsync(cancellationToken);
        return _fileCache is null ? listing : await _fileCache.ApplyListingAsync(printerId, listing, cancellationToken);
    }

    /// <summary>The full metadata of listed files; empty when the printer's connector lists everything at once.</summary>
    public async Task<IReadOnlyList<PrintMetadata>> GetPrinterFileDetailsAsync(
        Guid printerId, IReadOnlyList<string> fileNames, CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(printerId, out var session) || session.Session is not IFileDetails details)
        {
            return [];
        }

        var read = await details.GetFileDetailsAsync(fileNames, cancellationToken);
        if (_fileCache is not null)
        {
            await _fileCache.StoreDetailsAsync(printerId, read, cancellationToken);
        }

        return read;
    }

    /// <summary>A file's thumbnail, from the file cache when it holds one.</summary>
    public Task<PrintThumbnail?> GetThumbnailAsync(Guid printerId, string fileName, CancellationToken cancellationToken) =>
        GetThumbnailAsync(printerId, fileName, fromPrinter: false, cancellationToken);

    /// <summary>A file's thumbnail, from the file cache when it holds one.</summary>
    /// <param name="printerId">The printer.</param>
    /// <param name="fileName">A listed file, or the print job's name as the printer reports it.</param>
    /// <param name="fromPrinter">
    /// Bypasses the file cache. For the current print job, whose file may have been replaced since the last
    /// listing the cache checks against.
    /// </param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<PrintThumbnail?> GetThumbnailAsync(
        Guid printerId, string fileName, bool fromPrinter, CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(printerId, out var session) || session.Session is not IThumbnails thumbnails)
        {
            return null;
        }

        if (fromPrinter || _fileCache is null)
        {
            return await thumbnails.GetThumbnailAsync(fileName, cancellationToken);
        }

        if (await _fileCache.GetThumbnailAsync(printerId, fileName, cancellationToken) is { } cached)
        {
            return cached;
        }

        var thumbnail = await thumbnails.GetThumbnailAsync(fileName, cancellationToken);
        if (thumbnail is not null)
        {
            await _fileCache.StoreThumbnailAsync(printerId, fileName, thumbnail, cancellationToken);
        }

        return thumbnail;
    }

    public async Task PausePrintJobAsync(Guid printerId, CancellationToken cancellationToken)
    {
        if (TryGetCapability<IPrintControl>(printerId, "pause print job", out var control))
        {
            await control.PausePrintAsync(cancellationToken);
        }
    }

    public async Task ResumePrintJobAsync(Guid printerId, CancellationToken cancellationToken)
    {
        if (TryGetCapability<IPrintControl>(printerId, "resume print job", out var control))
        {
            await control.ResumePrintAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Sends a command the user typed to the printer. The command is checked here again, whatever the client did, because
    /// it is the one thing that reaches a printer unchanged. It is written to the console first, so what the printer
    /// answers reads as a reply.
    /// </summary>
    /// <exception cref="ValidationException">The command is not one line of plain text, or the printer cannot take commands.</exception>
    /// <exception cref="NotFoundException">The printer has no running session.</exception>
    public async Task SendConsoleCommandAsync(Guid printerId, string command, CancellationToken cancellationToken)
    {
        if (!ConsoleCommandRules.TryNormalize(command, out var normalized, out var error))
        {
            throw new ValidationException("command", error!);
        }

        if (!_sessions.TryGetValue(printerId, out var session))
        {
            throw new NotFoundException("The printer is not connected.");
        }

        if (session.Session is not IConsoleInput input)
        {
            throw new ValidationException("command", "This printer's connector cannot send commands.");
        }

        // Only the name of the command is logged: its arguments can hold a secret (a Wi-Fi password in M587, for example).
        var commandName = normalized.Split(' ', 2)[0];
        _logger.LogInformation("Sending console command {CommandName} to printer {PrinterId}", commandName, printerId);
        _console?.Echo(printerId, $"> {normalized}");
        await input.SendCommandAsync(normalized, cancellationToken);
    }

    public async Task StartPrintJobAsync(Guid printerId, string fileName, CancellationToken cancellationToken)
    {
        if (TryGetCapability<IPrintControl>(printerId, $"start print job '{fileName}'", out var control))
        {
            await control.StartPrintAsync(fileName, cancellationToken);
        }
    }

    public async Task StopPrintJobAsync(Guid printerId, CancellationToken cancellationToken)
    {
        if (TryGetCapability<IPrintControl>(printerId, "stop print job", out var control))
        {
            await control.StopPrintAsync(cancellationToken);
        }
    }

    public async Task UploadFileAsync(Guid printerId, Stream stream, string fileName, CancellationToken cancellationToken)
    {
        if (TryGetCapability<IFileStorage>(printerId, $"upload file '{fileName}'", out var files))
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

    private bool TryGetCapability<T>(Guid printerId, string operation, out T capability)
        where T : class
    {
        if (!_sessions.TryGetValue(printerId, out var session))
        {
            _logger.LogWarning("Cannot {Operation}: no active session for printer {PrinterId}", operation, printerId);
            capability = null!;
            return false;
        }

        if (session.Session is not T supported)
        {
            _logger.LogWarning("Cannot {Operation}: the connector for printer {PrinterId} does not support it", operation, printerId);
            capability = null!;
            return false;
        }

        capability = supported;
        return true;
    }

    private void StartSession(PrinterProfile profile)
    {
        // A printer whose connector is missing must not stop the other printers from starting.
        if (!_registry.TryGet(profile.ConnectorId, out var connector))
        {
            _logger.LogError("Cannot start printer {PrinterId}: connector '{ConnectorId}' is not available",
                profile.PrinterId, profile.ConnectorId);
            return;
        }

        _logger.LogInformation("Starting session for printer {PrinterId} ({ConnectorId})",
            profile.PrinterId, connector.Descriptor.Id);

        var context = new ConnectorContext(
            _loggerFactory.CreateLogger($"Connector.{connector.Descriptor.Id}"),
            Path.Combine(_dataRoot, connector.Descriptor.Id, profile.PrinterId.ToString("N")));

        var session = connector.CreateSession(profile, context);

        // Before the session runs, so its first lines are not lost.
        if (_console is not null && session is IConsoleOutput consoleOutput)
        {
            consoleOutput.SetConsoleSink(_console.CreateSink(profile.PrinterId));
        }

        var cts = new CancellationTokenSource();

        // Until the connector reports, clients would otherwise keep the previous session's state (or none).
        // The connector replaces this with Offline if its attempt fails.
        _telemetry.Send(new PrinterTelemetry(profile.PrinterId) { State = UniversalPrinterState.Connecting });

        var task = Task.Run(async () =>
        {
            try
            {
                await session.RunAsync(_telemetry, cts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Session loop cancelled for printer {PrinterId}", profile.PrinterId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled fault in printer worker for printer {PrinterId} ({ConnectorId})",
                    profile.PrinterId, profile.ConnectorId);
            }
        });

        _sessions[profile.PrinterId] = new RunningPrinter(profile, session, cts, task);
    }

    private async Task StopSessionAsync(Guid printerId)
    {
        if (_sessions.TryRemove(printerId, out var session))
        {
            _logger.LogDebug("Stopping session for printer {PrinterId}", printerId);
            session.Cts.Cancel();
            try
            {
                // A connector that ignores cancellation must not hold up every later change of printers.
                await session.ExecutionTask.WaitAsync(_stopTimeout);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Session for printer {PrinterId} ({ConnectorId}) did not stop within {TimeoutSeconds}s; abandoning it",
                    printerId, session.Profile.ConnectorId, _stopTimeout.TotalSeconds);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Printer session {PrinterId} faulted while stopping", printerId);
            }
            catch (OperationCanceledException)
            {
                // Ignored on cancel
            }

            await session.Session.DisposeAsync();
            session.Cts.Dispose();
            _logger.LogInformation("Stopped session for printer {PrinterId}", printerId);
        }
    }
}
