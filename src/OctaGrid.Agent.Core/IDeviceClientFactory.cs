using OctaGrid.Protocol.Agents;

namespace OctaGrid.Agent.Core;

public interface IDeviceClientFactory
{
    bool CanHandle(string protocol);

    IDeviceClient Create(DeviceProfile profile);
}
