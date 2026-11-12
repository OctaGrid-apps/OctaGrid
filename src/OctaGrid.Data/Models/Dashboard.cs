using OctaGrid.Protocol.Dashboards;

namespace OctaGrid.Data.Models;

public sealed class Dashboard : Entity
{
    public string Name { get; set; } = string.Empty;
    public Guid WorkspaceId { get; set; }
    public DashboardType Type { get; set; }
    public bool IsDefault { get; set; }

    // Navigation props
    public Workspace? Workspace { get; set; }

    public ICollection<DashboardWidget> Widgets { get; set; } = [];
}
