namespace OctaGrid.Data.Abstractions.Models;

public sealed class Workspace : Entity
{
    public string Name { get; set; } = string.Empty;

    // Navigation props
    public ICollection<WorkspaceMember> Members { get; set; } = [];

    public ICollection<Agent> Agents { get; set; } = [];

    public ICollection<Dashboard> Dashboards { get; set; } = [];

    public ICollection<Printer> Printers { get; set; } = [];
}
