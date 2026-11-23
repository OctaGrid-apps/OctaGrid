namespace OctaGrid.Data.Models;

/// <summary>
/// How a dashboard is shown. Stored by name: add members, never rename them.
/// Services map it to the wire type <c>Protocol.Dashboards.DashboardType</c>.
/// </summary>
public enum DashboardKind
{
    /// <summary>Every printer of the workspace as a card, which can be sorted and filtered.</summary>
    List,

    /// <summary>Widgets placed freely on a grid.</summary>
    Layout
}
