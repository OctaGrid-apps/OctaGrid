namespace OctaGrid.Protocol.Printers;

public record CreatePrinterRequest(string Name, string Url, string ConnectorId, string? ApiKey = null, bool Enabled = true);
