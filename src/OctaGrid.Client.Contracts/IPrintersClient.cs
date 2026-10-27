using OctaGrid.Protocol.Printers;

namespace OctaGrid.Client.Contracts;

public interface IPrintersClient
{
    Task<PrinterResponse> CreatePrinter(Guid workspaceId, CreatePrinterRequest request, CancellationToken cancellationToken);

    Task DeletePrinter(Guid printerId, CancellationToken cancellationToken);

    Task<PrinterResponse> GetPrinter(Guid workspaceId, Guid printerId, CancellationToken cancellationToken);

    Task<List<PrinterResponse>> GetPrintersList(Guid workspaceId, GetPrintersQuery query, CancellationToken cancellationToken);

    Task SetPrinterEnabled(Guid printerId, bool state, CancellationToken cancellationToken);

    Task<PrinterResponse> UpdateIdentity(Guid printerId, UpdatePrinterIdentityRequest request, CancellationToken cancellationToken);

    Task<PrinterResponse> UpdatePrinter(Guid printerId, UpdatePrinterRequest request, CancellationToken cancellationToken);
}
