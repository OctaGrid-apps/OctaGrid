using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Agent.Core;

public interface IMessageHandler
{
    void Handle(EntityChanged entity);
}
