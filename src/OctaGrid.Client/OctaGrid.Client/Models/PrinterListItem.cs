namespace OctaGrid.Client.Models;

public record PrinterListItem(Guid Id, string Name, string Url, string ConnectionProtocol, string ConnectionState, string? EncryptedApiKey, bool Enabled, long Version) : IPrinterListItem;
