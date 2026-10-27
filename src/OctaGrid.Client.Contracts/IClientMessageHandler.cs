using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Contracts;

public interface IClientMessageHandler
{
    void Handle(EntityChanged entity);

    void Handle(PrinterTelemetry telemetryMessage);
}
