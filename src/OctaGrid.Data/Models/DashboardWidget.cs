namespace OctaGrid.Data.Models;

public sealed class DashboardWidget : Entity
{
    public Guid DashboardId { get; set; }
    public Guid PrinterId { get; set; }
    public WidgetType WidgetType { get; set; }
    public int GridX { get; set; }
    public int GridY { get; set; }
    public int GridWidth { get; set; }
    public int GridHeight { get; set; }
    public string ConfigJson { get; set; } = string.Empty;

    // The printer is another aggregate, referred to by id.
    public Dashboard? Dashboard { get; set; }
}
