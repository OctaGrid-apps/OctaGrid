namespace OctaGrid.Extensions.Models;

public record PrintStatus
{
    public string? FileName { get; set; }
    public TimeOnly PrintTime { get; set; }
    public TimeOnly PrintTimeTotal { get; set; }
    public int? LayersCount { get; set; }
    public int? CurrentLayer { get; set; }
    public double? Progress { get; set; }

    // The slicer's predicted total duration for this file (server.files.metadata's
    // "estimated_time"), fetched on demand — not pushed by notify_status_update. Distinct from
    // PrintTimeTotal, which is the actual elapsed duration reported by print_stats.
    public TimeOnly? EstimatedPrintTime { get; set; }
}
