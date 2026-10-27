namespace OctaGrid.Client.Models;

public record Agent
{
    public Guid Id { get; set; } = Guid.Empty;
    public string Name { get; set; } = string.Empty;
}
