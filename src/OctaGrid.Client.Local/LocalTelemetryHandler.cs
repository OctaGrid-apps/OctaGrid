using OctaGrid.Agent.Core;
using OctaGrid.Client.Contracts;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Local;

internal sealed class LocalTelemetryHandler : ITelemetryService
{
    private readonly IClientMessageHandler _messageHandler;

    public LocalTelemetryHandler(IClientMessageHandler messageHandler)
    {
        _messageHandler = messageHandler;
    }

    public void Send(PrinterTelemetry printerTelemetry)
    {
        _messageHandler.Handle(printerTelemetry);
    }
}
