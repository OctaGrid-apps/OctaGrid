namespace OctaGrid.Data.Models;

/// <summary>
/// The kind of a dashboard, which decides how it is shown. Stored by name, so members may be added but never
/// renamed. The wire type is <c>Protocol.Dashboards.DashboardType</c>; services map between the two.
/// </summary>
public enum DashboardKind
{
    /// <summary>Every printer of the workspace as a card, which can be sorted and filtered.</summary>
    List,

    /// <summary>Widgets placed freely on a grid.</summary>
    Layout
}
