using OctaGrid.Agent.Core.Connectors;
using OctaGrid.Extensions.Abstractions;
using OctaGrid.Protocol.Agents;
using OctaGrid.Protocol.Extensions;
using OctaGrid.Protocol.Messaging;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Agent.Core.Tests;

internal sealed class FakeTelemetry : ITelemetrySink
{
    public List<PrinterTelemetry> Sent { get; } = [];

    public void Send(PrinterTelemetry telemetry) => Sent.Add(telemetry);
}

/// <summary>A minimal SDK session with every capability, recording what the host asks of it.</summary>
internal class FakeSession : IPrinterSession, IPrintControl, IFileStorage, IThumbnails
{
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<string> Calls { get; } = [];

    public ITelemetrySink? Telemetry { get; private set; }

    public bool Disposed { get; private set; }

    public Task Started => _started.Task;

    public async Task RunAsync(ITelemetrySink telemetry, CancellationToken cancellationToken)
    {
        Telemetry = telemetry;
        _started.TrySetResult();
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken) =>
        Task.FromResult(ConnectionTestResult.Ok());

    public Task RequestStateAsync(CancellationToken cancellationToken) => Record("state");

    public Task StartPrintAsync(string fileName, CancellationToken cancellationToken) => Record($"start:{fileName}");

    public Task PausePrintAsync(CancellationToken cancellationToken) => Record("pause");

    public Task ResumePrintAsync(CancellationToken cancellationToken) => Record("resume");

    public Task StopPrintAsync(CancellationToken cancellationToken) => Record("stop");

    public Task<IReadOnlyList<PrintMetadata>> ListFilesAsync(CancellationToken cancellationToken)
    {
        Calls.Add("list");
        return Task.FromResult<IReadOnlyList<PrintMetadata>>([new PrintMetadata("a.gcode", 10, null, null, null)]);
    }

    public Task UploadFileAsync(Stream content, string fileName, CancellationToken cancellationToken) =>
        Record($"upload:{fileName}");

    public Task DeleteFileAsync(string fileName, CancellationToken cancellationToken) => Record($"delete:{fileName}");

    public Task<PrintThumbnail?> GetThumbnailAsync(string fileName, CancellationToken cancellationToken)
    {
        Calls.Add($"thumb:{fileName}");
        return Task.FromResult<PrintThumbnail?>(new PrintThumbnail("image/png", [9]));
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }

    private Task Record(string call)
    {
        Calls.Add(call);
        return Task.CompletedTask;
    }
}

/// <summary>A session that supports no optional capability.</summary>
internal sealed class MinimalSession : IPrinterSession
{
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Started => _started.Task;

    public async Task RunAsync(ITelemetrySink telemetry, CancellationToken cancellationToken)
    {
        _started.TrySetResult();
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken) =>
        Task.FromResult(ConnectionTestResult.Ok());

    public Task RequestStateAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class FakeConnector(string id, Func<PrinterProfile, IPrinterSession> create) : IConnector
{
    public List<PrinterProfile> Created { get; } = [];

    public ConnectorDescriptor Descriptor { get; } = new(
        id, "Fake", "1.0.0", "Tests", ConnectorCapabilities.None, [], []);

    public IPrinterSession CreateSession(PrinterProfile profile, IConnectorContext context)
    {
        Created.Add(profile);
        return create(profile);
    }
}

internal sealed class InMemoryStateStore : IExtensionStateStore
{
    private HashSet<string> _disabled = new(StringComparer.OrdinalIgnoreCase);

    public int Saves { get; private set; }

    public IReadOnlySet<string> LoadDisabled() => new HashSet<string>(_disabled, StringComparer.OrdinalIgnoreCase);

    public void SaveDisabled(IReadOnlyCollection<string> disabledIds)
    {
        Saves++;
        _disabled = new HashSet<string>(disabledIds, StringComparer.OrdinalIgnoreCase);
    }
}
