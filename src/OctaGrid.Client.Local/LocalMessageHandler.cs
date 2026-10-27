using OctaGrid.Application.Telemetry;
using OctaGrid.Client.Contracts;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Local;

internal sealed class LocalMessageHandler : IMessagePublisher
{
    private readonly EntityChangeFeed _feed;
    private readonly IClientMessageHandler _messageHandler;

    public LocalMessageHandler(IClientMessageHandler messageHandler, EntityChangeFeed feed)
    {
        _messageHandler = messageHandler;
        _feed = feed;
    }

    public void Publish(EntityChanged entity)
    {
        _messageHandler.Handle(entity);
        _feed.Write(entity);
    }
}
