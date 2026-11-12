using OctaGrid.Extensions.Models;


namespace OctaGrid.Agent.Core;

public interface IPrinterProfileSource
{
    Task<IReadOnlyList<PrinterProfile>> GetProfilesAsync(CancellationToken cancellationToken);
}
