namespace OctaGrid.Client.Models;

public record Printer()
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ConnectorId { get; set; } = string.Empty;

    /// <summary>Values for the connector's non-secret settings, by key.</summary>
    public Dictionary<string, string> Settings { get; set; } = [];

    /// <summary>Values for the connector's secret settings, by key.</summary>
    public Dictionary<string, string> Secrets { get; set; } = [];

    public bool Enabled { get; set; } = true;
}
