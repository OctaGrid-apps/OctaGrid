using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Data;
using OctaGrid.Data.Models;
using OctaGrid.Protocol.AgentLink.Notifications;

namespace OctaGrid.Application.Services;

/// <summary>
/// Keeps the <see cref="PrintJob"/>s of printers in step with the state changes their agents report. Called for an
/// agent, not for a user: the check is that the agent reporting is the printer's agent.
/// </summary>
public sealed class PrintJobsService
{
    private readonly OctaGridContext _context;
    private readonly ILogger<PrintJobsService> _logger;

    public PrintJobsService(OctaGridContext context, ILogger<PrintJobsService>? logger = null)
    {
        _context = context;
        _logger = logger ?? NullLogger<PrintJobsService>.Instance;
    }

    /// <summary>Applies a reported state change to the printer's print jobs.</summary>
    /// <param name="agentId">The agent that reported it.</param>
    /// <param name="change">What it reported.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Whether a job started or ended, so its events wait in the outbox.</returns>
    public async Task<bool> RecordStateChange(Guid agentId, PrinterStateChanged change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        var printer = await _context.Printers
            .AsNoTracking()
            .Where(p => p.Id == change.PrinterId)
            .Select(p => new { p.WorkspaceId, p.AgentId })
            .FirstOrDefaultAsync(cancellationToken);

        // A printer that was deleted or moved to another agent since: what the old agent says about it no longer counts.
        if (printer is null || printer.AgentId != agentId)
        {
            _logger.LogDebug("Ignored a state change of printer {PrinterId} from agent {AgentId}, which does not run it",
                change.PrinterId, agentId);
            return false;
        }

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
