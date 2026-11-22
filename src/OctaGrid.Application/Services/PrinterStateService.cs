using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Data;
using OctaGrid.Data.Models;
using OctaGrid.Protocol.AgentLink.Notifications;

namespace OctaGrid.Application.Services;

/// <summary>
/// Applies the state changes an agent reports to the printer's <see cref="PrinterStatus"/> and
/// <see cref="PrintJob"/>s. Called on behalf of an agent, not a user, so it checks that the
/// reporting agent is the printer's agent instead of checking permissions.
/// </summary>
public sealed class PrinterStateService
{
    private readonly OctaGridContext _context;
    private readonly ILogger<PrinterStateService> _logger;

    public PrinterStateService(OctaGridContext context, ILogger<PrinterStateService>? logger = null)
    {
        _context = context;
        _logger = logger ?? NullLogger<PrinterStateService>.Instance;
    }

    /// <summary>Applies a reported state change to the printer's status and print jobs.</summary>
    /// <param name="agentId">The agent that reported it.</param>
    /// <param name="change">What it reported.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>True when changes were saved, including any print job events written to the outbox.</returns>
    public async Task<bool> RecordStateChange(Guid agentId, PrinterStateChanged change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        var printer = await _context.Printers
            .Include(p => p.Status)
            .FirstOrDefaultAsync(p => p.Id == change.PrinterId, cancellationToken);

        // The printer may have been deleted or moved to another agent since the report was sent.
        if (printer is null || printer.AgentId != agentId)
        {
            _logger.LogDebug("Ignored a state change of printer {PrinterId} from agent {AgentId}, which does not run it",
                change.PrinterId, agentId);
            return false;
        }

        // Updates only the status row, so the printer's Version does not change.
        printer.ObserveState(change.Current, change.OccurredAt);

        // There is at most one; if a fault ever left more, the older ones are closed as of unknown outcome.
        var running = (await _context.PrintJobs
                .Where(j => j.PrinterId == change.PrinterId && j.Outcome == null)
                .ToListAsync(cancellationToken))
            .OrderBy(j => j.StartedAt)
            .ToList();
        foreach (var stale in running.SkipLast(1))
        {
            stale.Finish(PrintJobOutcome.Unknown, change.OccurredAt);
        }

        var current = running.LastOrDefault();
        var started = PrintJob.Follow(current, printer.WorkspaceId, change.PrinterId, change.Current, change.FileName, change.OccurredAt);
        if (started is not null)
        {
            _context.PrintJobs.Add(started);
        }

        if (!_context.ChangeTracker.HasChanges())
        {
            return false;
        }

        await _context.SaveChangesAsync(cancellationToken);

        if (current is { IsRunning: false })
        {
            _logger.LogInformation("Print job {PrintJobId} on printer {PrinterId} ended: {Outcome}",
                current.Id, current.PrinterId, current.Outcome);
        }

        if (started is not null)
        {
            _logger.LogInformation("Print job {PrintJobId} started on printer {PrinterId}", started.Id, started.PrinterId);
        }

        return true;
    }
}
