using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Application.Telemetry;

public interface IMessagePublisher
{
    public void Publish(EntityChanged entity);
}
