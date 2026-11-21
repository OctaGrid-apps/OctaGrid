using OctaGrid.Data.Models;

namespace OctaGrid.Data.Events;

/// <summary>A printer started a print job.</summary>
/// <param name="PrintJobId">The job.</param>
/// <param name="WorkspaceId">The workspace of the printer.</param>
/// <param name="PrinterId">The printer that prints it.</param>
/// <param name="FileName">The file being printed, if the printer reported it.</param>
public sealed record PrintJobStarted(Guid PrintJobId, Guid WorkspaceId, Guid PrinterId, string? FileName) : DomainEvent;

/// <summary>A print job ended.</summary>
/// <param name="PrintJobId">The job.</param>
/// <param name="WorkspaceId">The workspace of the printer.</param>
/// <param name="PrinterId">The printer that printed it.</param>
/// <param name="FileName">The file that was printed, if the printer reported it.</param>
/// <param name="Outcome">How it ended.</param>
/// <param name="StartedAt">When it started; <see cref="DomainEvent.OccurredAt"/> is when it ended.</param>
public sealed record PrintJobFinished(
    Guid PrintJobId,
    Guid WorkspaceId,
    Guid PrinterId,
    string? FileName,
    PrintJobOutcome Outcome,
    DateTimeOffset StartedAt) : DomainEvent;
