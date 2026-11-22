using OctaGrid.Application.Telemetry;
using OctaGrid.Client.Contracts;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Local;

/// <summary>Hands what the backend publishes straight to the client, which runs in the same process.</summary>
internal sealed class LocalMessageHandler : IMessagePublisher
{
    private readonly IClientMessageHandler _messageHandler;

    public LocalMessageHandler(IClientMessageHandler messageHandler)
    {
        _messageHandler = messageHandler;
    }

    public void Publish(EntityChanged entity) => _messageHandler.Handle(entity);
}
