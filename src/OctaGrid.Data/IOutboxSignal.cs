namespace OctaGrid.Data;

/// <summary>
/// Told by <see cref="OctaGridContext"/> after a save that wrote domain events to the outbox,
/// so they can be delivered without waiting for the next retry.
/// </summary>
public interface IOutboxSignal
{
    /// <summary>Reports that new outbox messages were saved. Must not block.</summary>
    void Signal();
}
