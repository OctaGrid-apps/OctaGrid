using OctaGrid.Protocol.Agents;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Agent.Core;

public interface IDeviceClient : IAsyncDisposable
{
    Guid DeviceId { get; }

    Task DeleteFileAsync(string fileName, CancellationToken cancellationToken);

    Task<IEnumerable<PrintMetadata>> GetPrinterFilesAsync(CancellationToken cancellationToken);

    Task PausePrintJobAsync(CancellationToken cancellationToken);

    Task ResumePrintJobAsync(CancellationToken cancellationToken);

    Task RunAsync(ITelemetryService telemetryService, CancellationToken cancellationToken);

    Task StartPrintJobAsync(string fileName, CancellationToken cancellationToken);

    Task StopPrintJobAsync(CancellationToken cancellationToken);

    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken);

    Task TriggerInitialStateAsync(CancellationToken cancellationToken);

    Task UploadFileAsync(Stream stream, string fileName, CancellationToken cancellationToken);
}
