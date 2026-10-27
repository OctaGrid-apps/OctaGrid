namespace OctaGrid.Client.Models;

public interface IPrinterListItem;

public record PrinterListItem(Guid Id, string Name, string Url, string ConnectionProtocol, string ConnectionState, string? EncryptedApiKey, bool Enabled, long Version) : IPrinterListItem;

public class AddPrinterListItem : IPrinterListItem
{
    public static readonly AddPrinterListItem Instance = new();

    private AddPrinterListItem()
    { }
}
