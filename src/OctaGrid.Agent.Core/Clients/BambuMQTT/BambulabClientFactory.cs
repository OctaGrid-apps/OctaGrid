using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Protocol.Agents;

namespace OctaGrid.Agent.Core.Clients.BambuMQTT;

public class BambulabClientFactory : IDeviceClientFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public BambulabClientFactory(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
    }

    public bool CanHandle(string protocol) =>
        string.Equals(protocol, "BambuMqtt", StringComparison.OrdinalIgnoreCase);

    public IDeviceClient Create(DeviceProfile profile) =>
        new MqttClient(profile, _loggerFactory.CreateLogger<MqttClient>());
}
