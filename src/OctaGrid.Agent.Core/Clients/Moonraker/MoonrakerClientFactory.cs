using OctaGrid.Protocol.Agents;

namespace OctaGrid.Agent.Core.Clients.Moonraker;

public class MoonrakerClientFactory : IDeviceClientFactory
{
    public bool CanHandle(string protocol) =>
        string.Equals(protocol, "Moonraker", StringComparison.OrdinalIgnoreCase);

    public IDeviceClient Create(DeviceProfile profile) =>
        new MoonrakerClient(profile);
}
