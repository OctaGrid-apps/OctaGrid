namespace OctaGrid.Protocol.Printers;

public record UpdatePrinterRequest(string? Name, string? Url, string? ConnectorId, string? ApiKey, bool Enabled = true);
