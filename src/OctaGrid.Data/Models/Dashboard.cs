using OctaGrid.Protocol.Dashboards;

namespace OctaGrid.Data.Models;

/// <summary>
/// A dashboard, part of the <see cref="Workspace"/> aggregate: it is added, removed and made the default only through
/// its workspace, which keeps the rules about the whole list.
/// </summary>
public sealed class Dashboard : Entity
{
    // For EF.
    private Dashboard()
    {
    }

    public string Name { get; private set; } = string.Empty;
    public Guid WorkspaceId { get; private set; }
    public DashboardType Type { get; private set; }

    /// <summary>Whether this is the dashboard the workspace opens with. Set only by <see cref="Workspace"/>.</summary>
    public bool IsDefault { get; internal set; }

    public ICollection<DashboardWidget> Widgets { get; private set; } = [];

    // The id is set here, not on save: several new dashboards can be tracked at once only with ids of their own.
    internal static Dashboard Create(Guid workspaceId, string name, DashboardType type, bool isDefault) => new()
    {
        Id = Guid.CreateVersion7(),
        WorkspaceId = workspaceId,
        Name = name,
        Type = type,
        IsDefault = isDefault,
    };

    public void Rename(string name) => Name = name;
}
