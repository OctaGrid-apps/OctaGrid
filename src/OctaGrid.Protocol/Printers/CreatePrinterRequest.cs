namespace OctaGrid.Protocol.Printers;

public record CreatePrinterRequest(string Name, string Url, string ConnectionProtocol, string? ApiKey = null, bool Enabled = true);
