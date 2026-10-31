namespace OctaGrid.Protocol.Printers;

public record UpdatePrinterRequest(string? Name, string? Url, string? ConnectionProtocol, string? ApiKey, bool Enabled = true);
