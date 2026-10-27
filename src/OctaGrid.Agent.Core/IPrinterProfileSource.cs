using OctaGrid.Protocol.Agents;

namespace OctaGrid.Agent.Core;

public interface IPrinterProfileSource
{
    Task<IReadOnlyList<DeviceProfile>> GetProfilesAsync(CancellationToken cancellationToken);
}
