using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Agent.Core;

public interface ITelemetryService
{
    void Send(PrinterTelemetry printerTelemetry);
}
