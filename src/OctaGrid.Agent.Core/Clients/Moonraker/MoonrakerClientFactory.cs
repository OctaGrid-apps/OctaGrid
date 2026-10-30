using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Protocol.Agents;

namespace OctaGrid.Agent.Core.Clients.Moonraker;

public class MoonrakerClientFactory : IDeviceClientFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public MoonrakerClientFactory(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
    }

    public bool CanHandle(string protocol) =>
        string.Equals(protocol, "Moonraker", StringComparison.OrdinalIgnoreCase);

    public IDeviceClient Create(DeviceProfile profile) =>
        new MoonrakerClient(profile, _loggerFactory);
}
