using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Contracts;

public interface IClientMessageHandler
{
    void Handle(EntityChanged entity);

    void Handle(PrinterTelemetry telemetryMessage);

    /// <summary>A new event was recorded for a printer. May be called from any thread.</summary>
    void Handle(PrinterEventRecorded recorded);
}
