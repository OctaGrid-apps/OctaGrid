using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Agent.Core;

public interface IPrinterProfileSource
{
    Task<IReadOnlyList<PrinterProfile>> GetProfilesAsync(CancellationToken cancellationToken);
}
