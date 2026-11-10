namespace OctaGrid.Agent.Core.Events;

/// <summary>
/// Limits that keep a printer's event history, and what connectors may put into a snapshot, bounded no
/// matter how an extension behaves.
/// </summary>
/// <param name="Capacity">How many events are kept per printer. The oldest are dropped first.</param>
/// <param name="MaxMessageLength">Longest event or condition message; longer ones are cut and end with an ellipsis.</param>
/// <param name="MaxCodeLength">Longest event or condition code; longer ones are cut.</param>
/// <param name="MaxConditions">Most conditions a snapshot may carry; the most serious are kept.</param>
/// <param name="Burst">How many events a printer may record at once before the rate limit applies.</param>
/// <param name="RefillInterval">How long it takes to earn back the right to record one more event.</param>
public sealed record PrinterEventLogOptions(
    int Capacity = 200,
    int MaxMessageLength = 256,
    int MaxCodeLength = 64,
    int MaxConditions = 10,
    int Burst = 20,
    TimeSpan? RefillInterval = null)
{
    /// <summary>The time between earned events once the burst is used up. Defaults to five seconds.</summary>
    public TimeSpan EffectiveRefillInterval => RefillInterval ?? TimeSpan.FromSeconds(5);
}
