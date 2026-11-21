using OctaGrid.Data.Events;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Data.Models;

/// <summary>
/// One print on one printer, from when the printer started printing a file to when it reported an end state.
/// An aggregate of its own: it refers to its printer by id and changes only through <see cref="Start"/>,
/// <see cref="Follow"/> and <see cref="Finish"/>, which raise the matching <see cref="DomainEvent"/>s.
/// </summary>
public sealed class PrintJob : Entity
{
    // For EF.
    private PrintJob()
    {
    }

    public Guid WorkspaceId { get; private set; }
    public Guid PrinterId { get; private set; }
    public string? FileName { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }

    /// <summary>How the job ended; null while it runs.</summary>
    public PrintJobOutcome? Outcome { get; private set; }

    public bool IsRunning => Outcome is null;

    /// <summary>Starts a job on a printer that has none running.</summary>
    public static PrintJob Start(Guid workspaceId, Guid printerId, string? fileName, DateTimeOffset startedAt)
    {
        var job = new PrintJob
        {
            // Set here, not on save, because the event carries it.
            Id = Guid.CreateVersion7(),
            WorkspaceId = workspaceId,
            PrinterId = printerId,
            FileName = string.IsNullOrEmpty(fileName) ? null : fileName,
            StartedAt = startedAt,
        };
        job.Raise(new PrintJobStarted(job.Id, workspaceId, printerId, job.FileName) { OccurredAt = startedAt });
        return job;
    }

    /// <summary>
    /// Applies the printer's new state to the job it runs (<paramref name="running"/>, or null when it runs none)
    /// and returns the job that is new, if the state started one.
    /// </summary>
    /// <remarks>
    /// Printing or paused starts a job. An end state (completed, failed, cancelled, or a printer fault) ends it;
    /// going idle, or printing another file, ends it as <see cref="PrintJobOutcome.Unknown"/>. Offline, connecting
    /// and preparing change nothing: a printer that drops off the network mid-print is usually still printing.
    /// </remarks>
    public static PrintJob? Follow(
        PrintJob? running, Guid workspaceId, Guid printerId, UniversalPrinterState state, string? fileName, DateTimeOffset at)
    {
        if (running is not null && !running.IsRunning)
        {
            throw new ArgumentException("The job has already ended.", nameof(running));
        }

        if (OutcomeOf(state) is { } outcome)
        {
            running?.Finish(outcome, at);
            return null;
        }

        if (state is not (UniversalPrinterState.Printing or UniversalPrinterState.Paused))
        {
            return null;
        }

        if (running is null)
        {
            return Start(workspaceId, printerId, fileName, at);
        }

        // Another file without an end state in between: the end of the first one was missed.
        if (running.FileName is { } current && !string.IsNullOrEmpty(fileName) && !string.Equals(current, fileName, StringComparison.Ordinal))
        {
            running.Finish(PrintJobOutcome.Unknown, at);
            return Start(workspaceId, printerId, fileName, at);
        }

        // Some printers report the file name only after they start printing.
        if (running.FileName is null && !string.IsNullOrEmpty(fileName))
        {
            running.FileName = fileName;
        }

        return null;
    }

    /// <summary>Ends a running job.</summary>
    /// <exception cref="InvalidOperationException">The job has already ended.</exception>
    public void Finish(PrintJobOutcome outcome, DateTimeOffset endedAt)
    {
        if (!IsRunning)
        {
            throw new InvalidOperationException($"Print job {Id} has already ended.");
        }

        Outcome = outcome;
        EndedAt = endedAt < StartedAt ? StartedAt : endedAt;
        Raise(new PrintJobFinished(Id, WorkspaceId, PrinterId, FileName, outcome, StartedAt) { OccurredAt = EndedAt.Value });
    }

    private static PrintJobOutcome? OutcomeOf(UniversalPrinterState state) => state switch
    {
        UniversalPrinterState.Completed => PrintJobOutcome.Completed,
        UniversalPrinterState.Failed or UniversalPrinterState.Error => PrintJobOutcome.Failed,
        UniversalPrinterState.Cancelled => PrintJobOutcome.Cancelled,
        UniversalPrinterState.Idle => PrintJobOutcome.Unknown,
        _ => null,
    };
}
