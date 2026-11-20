namespace OctaGrid.Agent.Core.Events;

/// <summary>Limits that keep a printer's telemetry history bounded however often its connector reports.</summary>
/// <param name="Capacity">
/// How many samples are kept per printer; the oldest are dropped first. With <see cref="MinInterval"/> at one second the
/// default covers 20 minutes, the same as Moonraker's own temperature store.
/// </param>
public sealed record PrinterTelemetryLogOptions(int Capacity = 1200)
{
    /// <summary>The shortest time between two stored samples. Snapshots in between still update the known values.</summary>
    public TimeSpan MinInterval { get; init; } = TimeSpan.FromSeconds(1);
}
