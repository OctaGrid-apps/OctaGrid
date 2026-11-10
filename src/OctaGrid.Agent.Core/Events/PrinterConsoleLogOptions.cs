namespace OctaGrid.Agent.Core.Events;

/// <summary>
/// Limits that keep a printer's console log bounded no matter how much a printer, or the connector that reads it, writes.
/// </summary>
/// <param name="Capacity">How many lines are kept per printer. The oldest are dropped first.</param>
/// <param name="MaxLineLength">Longest line; longer ones are cut and end with an ellipsis.</param>
/// <param name="Burst">How many lines a printer may write at once before the rate limit applies.</param>
/// <param name="LinesPerSecond">How many more lines per second are accepted once the burst is used up.</param>
public sealed record PrinterConsoleLogOptions(
    int Capacity = 500,
    int MaxLineLength = 500,
    int Burst = 200,
    double LinesPerSecond = 50);
