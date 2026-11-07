using System.Text.Json.Serialization;

namespace OctaGrid.Protocol.Dashboards;

/// <summary>The kind of dashboard, which decides how it is shown. New kinds are added here.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DashboardType
{
    /// <summary>Every printer of the workspace as a card, which can be sorted and filtered.</summary>
    List,

    /// <summary>Widgets placed freely on a grid.</summary>
    Layout
}
