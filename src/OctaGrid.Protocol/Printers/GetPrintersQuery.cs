namespace OctaGrid.Protocol.Printers;

public record PrinterResponse(Guid Id, string Name, string Url, string ConnectorId, string ConnectionState, string? EncryptedApiKey, bool Enabled, long Version);
