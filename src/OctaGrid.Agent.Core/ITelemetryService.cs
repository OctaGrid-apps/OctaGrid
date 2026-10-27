using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Agent.Core;

public interface ITelemetryService
{
    void Send(PrinterTelemetry printerTelemetry);
}

public interface ICommandService
{
    Task TriggerInitialStateAsync(Guid printerId, CancellationToken cancellationToken);

    Task TriggerInitialStateAsync(IEnumerable<Guid> printers, CancellationToken cancellationToken);
}
