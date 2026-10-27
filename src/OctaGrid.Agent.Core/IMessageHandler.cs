using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Agent.Core;

public interface IMessageHandler
{
    void Handle(EntityChanged entity);
}

public sealed class MessageHandler : IMessageHandler
{
    public void Handle(EntityChanged entity)
    {
    }
}
