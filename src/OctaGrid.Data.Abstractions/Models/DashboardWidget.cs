namespace OctaGrid.Data.Abstractions.Models;

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

    // Navigation props
    public Dashboard? Dashboard { get; set; }
    public Printer? Printer { get; set; }
}
