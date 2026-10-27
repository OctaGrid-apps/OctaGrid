using OctaGrid.Protocol.Agents;

namespace OctaGrid.Agent.Core.Clients.BambuMQTT;

public class BambulabClientFactory : IDeviceClientFactory
{
    public bool CanHandle(string protocol) =>
        string.Equals(protocol, "BambuMqtt", StringComparison.OrdinalIgnoreCase);

    public IDeviceClient Create(DeviceProfile profile) =>
        new MqttClient(profile);
}
