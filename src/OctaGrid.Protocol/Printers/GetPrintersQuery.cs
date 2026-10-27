namespace OctaGrid.Protocol.Printers;

public record PrinterResponse(Guid Id, string Name, string Url, string ConnectionProtocol, string ConnectionState, string? EncryptedApiKey, bool Enabled, long Version);
