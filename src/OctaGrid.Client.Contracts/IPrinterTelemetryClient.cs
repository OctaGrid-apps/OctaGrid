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
    /// After a reconnect: printers the client already has history for, with the time of its last sample, to get only
    /// what came after it.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="OctaGrid.Extensions.Models.Exceptions.ForbiddenException">The user may not view one of the printers.</exception>
    Task<IReadOnlyList<PrinterBackfill>> WatchAsync(
        IReadOnlyCollection<Guid> printerIds,
        IReadOnlyDictionary<Guid, DateTimeOffset>? resume,
        CancellationToken cancellationToken);
}
