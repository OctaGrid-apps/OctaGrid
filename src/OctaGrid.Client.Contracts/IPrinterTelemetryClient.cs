using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Contracts;

/// <summary>
/// Which printers this client shows. Live telemetry arrives only for those printers, through
/// <see cref="IClientMessageHandler"/>.
/// </summary>
public interface IPrinterTelemetryClient
{
    /// <summary>
    /// Replaces the printers this client shows, and returns the state and recent history of those it did not show
    /// before, plus those in <paramref name="resume"/>.
    /// </summary>
    /// <param name="printerIds">Every printer the client shows; empty to show none.</param>
    /// <param name="resume">
    /// Printers the client wants the state of again although it may already show them (something new started showing
    /// them, or the connection was restored), each with the time of the last history sample the client has, to get only
    /// what came after it; <see cref="DateTimeOffset.MinValue"/> for all of it.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>A printer the user may not view comes back offline instead of failing the call.</remarks>
    Task<IReadOnlyList<PrinterBackfill>> WatchAsync(
        IReadOnlyCollection<Guid> printerIds,
        IReadOnlyDictionary<Guid, DateTimeOffset>? resume,
        CancellationToken cancellationToken);
}
