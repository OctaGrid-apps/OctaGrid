using OctaGrid.Data.Events;
using OctaGrid.Extensions.Models.Exceptions;
using OctaGrid.Protocol.Dashboards;

namespace OctaGrid.Data.Models;

/// <summary>
/// The root of the workspace aggregate: the workspace, its members and its list of dashboards. Rules that span the
/// list (a workspace always has a dashboard, exactly one of them is the default) are kept here, and a change made
/// through these methods also rotates the workspace's <see cref="Entity.Version"/> (see <see cref="OctaGridContext"/>),
/// so two such changes made at the same time cannot both be saved.
/// Printers and agents belong to a workspace but are aggregates of their own, referring to it by id.
/// </summary>
public sealed class Workspace : Entity
{
    /// <summary>The name of the dashboard every new workspace starts with.</summary>
    public const string DefaultDashboardName = "Default";

    private readonly List<Dashboard> _dashboards = [];
    private readonly List<WorkspaceMember> _members = [];

    // For EF.
    private Workspace()
    {
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>The dashboards; only those loaded with the workspace (<c>Include</c>) are here.</summary>
    public IReadOnlyCollection<Dashboard> Dashboards => _dashboards;

    /// <summary>The members; only those loaded with the workspace (<c>Include</c>) are here.</summary>
    public IReadOnlyCollection<WorkspaceMember> Members => _members;

    /// <summary>Creates a workspace owned by <paramref name="ownerId"/>, with one default list dashboard.</summary>
    public static Workspace Create(string name, Guid ownerId, DateTimeOffset createdAt)
    {
        // The id is set here, not on save, so the dashboard and member created with it point to it from the start.
        var workspace = new Workspace { Id = Guid.CreateVersion7(), Name = name };
        workspace.AddMember(ownerId, Role.Owner, createdAt);
        workspace.AddDashboard(DefaultDashboardName, DashboardType.List);
        return workspace;
    }

    public void Rename(string name)
    {
        if (string.Equals(Name, name, StringComparison.Ordinal))
        {
            return;
        }

        Name = name;
        Raise(new WorkspaceRenamed(Id, name));
    }

    /// <summary>Adds a member. Needs the members loaded.</summary>
    /// <exception cref="ValidationException">The user is already a member.</exception>
    public WorkspaceMember AddMember(Guid userId, Role role, DateTimeOffset joinedAt)
    {
        if (_members.Any(m => m.UserId == userId))
        {
            throw new ValidationException(nameof(userId), "The user is already a member of this workspace.");
        }

        var member = WorkspaceMember.Create(userId, role, joinedAt);
        _members.Add(member);
        return member;
    }

    /// <summary>Adds a dashboard. The first one becomes the default. Needs the dashboards loaded.</summary>
    public Dashboard AddDashboard(string name, DashboardType type)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ValidationException(nameof(Dashboard.Type), $"Unknown dashboard type '{type}'.");
        }

        var dashboard = Dashboard.Create(Id, name, type, isDefault: _dashboards.Count == 0);
        _dashboards.Add(dashboard);
        return dashboard;
    }

    /// <summary>Makes a dashboard of this workspace the default, and the previous default an ordinary one.</summary>
    /// <exception cref="NotFoundException">The dashboard is not one of this workspace's.</exception>
    public void SetDefaultDashboard(Guid dashboardId)
    {
        var chosen = FindDashboard(dashboardId);
        foreach (var dashboard in _dashboards)
        {
            dashboard.IsDefault = dashboard == chosen;
        }
    }

    /// <summary>
    /// Removes a dashboard. The last one cannot be removed; when the default is removed, the first of the others by
    /// name becomes the default.
    /// </summary>
    /// <exception cref="NotFoundException">The dashboard is not one of this workspace's.</exception>
    /// <exception cref="ValidationException">It is the workspace's only dashboard.</exception>
    public Dashboard RemoveDashboard(Guid dashboardId)
    {
        var dashboard = FindDashboard(dashboardId);
        if (_dashboards.Count == 1)
        {
            throw new ValidationException(nameof(dashboardId), "A workspace needs at least one dashboard, so the last one cannot be deleted.");
        }

        _dashboards.Remove(dashboard);
        if (dashboard.IsDefault)
        {
            // Ordered in memory so the choice is the same for every database provider.
            _dashboards.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).First().IsDefault = true;
        }

        return dashboard;
    }

    /// <summary>Gives a workspace that has no dashboard (from an older database) its default one.</summary>
    /// <returns>Whether one was added.</returns>
    public bool EnsureDashboard()
    {
        if (_dashboards.Count > 0)
        {
            return false;
        }

        AddDashboard(DefaultDashboardName, DashboardType.List);
        return true;
    }

    private Dashboard FindDashboard(Guid dashboardId) =>
        _dashboards.FirstOrDefault(d => d.Id == dashboardId) ?? throw new NotFoundException(nameof(Dashboard), dashboardId);
}
