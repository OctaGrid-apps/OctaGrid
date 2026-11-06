using OctaGrid.Protocol.Printers;

namespace OctaGrid.Client.Models;

internal static class Extensions
{
    public static PrinterListItem ToPrinterListItem(this PrinterResponse printer) =>
        new(printer.Id, printer.Name, printer.Url, printer.ConnectorId, printer.ConnectionState, printer.EncryptedApiKey, printer.Enabled, printer.Version);
}
