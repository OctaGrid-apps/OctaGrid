using OctaGrid.Protocol.Agents;

namespace OctaGrid.Extensions.Abstractions;

/// <summary>
/// A live connection to one printer. Optional features are separate interfaces that the session
/// also implements when the printer supports them: <see cref="IPrintControl"/>,
/// <see cref="IFileStorage"/>, <see cref="IThumbnails"/> and <see cref="ICustomCommands"/>.
/// </summary>
public interface IPrinterSession : IAsyncDisposable
{
    /// <summary>
    /// Connects to the printer and reports its state until <paramref name="cancellationToken"/> is
    /// cancelled. Implementations reconnect on failure and report <c>Offline</c> while disconnected.
    /// </summary>
    Task RunAsync(ITelemetrySink telemetry, CancellationToken cancellationToken);

    /// <summary>A one-shot check run before a printer is saved. Must not throw for expected failures.</summary>
    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken);

    /// <summary>Asks the session to publish the printer's full current state again.</summary>
    Task RequestStateAsync(CancellationToken cancellationToken);
}
