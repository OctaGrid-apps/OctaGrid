using OctaGrid.Client.Contracts;
using OctaGrid.Extensions.Abstractions;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Client.Local;

internal sealed class LocalTelemetryHandler : ITelemetrySink
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
