using OctaGrid.Application.Agents;
using OctaGrid.Client.Contracts;
using OctaGrid.Protocol.AgentLink;
using OctaGrid.Protocol.AgentLink.Notifications;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Local;

/// <summary>
/// Passes what the local agent reports on to the UI: telemetry, recorded events and console lines go to the
/// client's message handler, and a changed connector list refreshes the <see cref="LocalConnectorCache"/>.
/// </summary>
internal sealed class LocalAgentNotifications
{
    private readonly LocalConnectorCache _connectors;
    private readonly IAgentLink _link;
    private readonly IClientMessageHandler _messageHandler;

    public LocalAgentNotifications(IAgentLink link, IClientMessageHandler messageHandler, LocalConnectorCache connectors)
    {
        _link = link;
        _messageHandler = messageHandler;
        _connectors = connectors;
    }

    /// <summary>Starts listening.</summary>
    /// <returns>Dispose it to stop.</returns>
    public IDisposable Start() => _link.Subscribe(Handle);

    // Called on the agent's threads; the message handler is written for that.
    private void Handle(AgentNotificationEnvelope envelope)
    {
        switch (envelope.Notification)
        {
            case PrinterTelemetryReported telemetry:
                _messageHandler.Handle(telemetry.Telemetry);
                break;
            case PrinterEventRecorded recorded:
                _messageHandler.Handle(recorded);
                break;
            case PrinterConsoleLineRecorded recorded:
                _messageHandler.Handle(recorded);
                break;
            case ConnectorsChanged changed:
                _connectors.Set(changed.Connectors);
                break;
        }
    }
}
