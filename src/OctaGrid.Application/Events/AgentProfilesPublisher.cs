using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Application.Services;
using OctaGrid.Data;
using OctaGrid.Data.Events;
using OctaGrid.Extensions.Models.Exceptions;

namespace OctaGrid.Application.Events;

/// <summary>
/// Sends an agent its printers again whenever a saved change affects one of them, so its set always equals the
/// saved printers assigned to it. Sending them when an agent connects is the link host's part.
/// </summary>
public sealed class AgentProfilesPublisher : IDomainEventHandler
{
    private readonly OctaGridContext _context;
    private readonly ILogger<AgentProfilesPublisher> _logger;
    private readonly AgentProfilesService _profiles;

    public AgentProfilesPublisher(OctaGridContext context, AgentProfilesService profiles, ILogger<AgentProfilesPublisher>? logger = null)
    {
        _context = context;
        _profiles = profiles;
        _logger = logger ?? NullLogger<AgentProfilesPublisher>.Instance;
    }

    /// <inheritdoc />
    public async Task HandleAsync(DomainEvent domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        Guid?[] agents = domainEvent switch
        {
            PrinterAdded e => [e.AgentId],
            PrinterRemoved e => [e.AgentId],
            PrinterAgentChanged e => [e.AgentId, e.PreviousAgentId],
            PrinterChanged e => [await AgentOf(e.PrinterId, cancellationToken)],
            _ => [],
        };

        foreach (var agentId in agents.OfType<Guid>().Distinct())
        {
            try
            {
                await _profiles.Push(agentId, cancellationToken);
            }
            catch (AgentUnavailableException)
            {
                // Not a failure to retry: the agent gets the complete set when it connects.
                _logger.LogInformation("Agent {AgentId} is not connected; it gets its printers when it connects", agentId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // Not retried: a failed event holds back the events after it.
                // The next change or reconnect sends the complete set again.
                _logger.LogError(ex, "Could not send agent {AgentId} its printers", agentId);
            }
        }
    }

    // The printer may be gone by the time the event is delivered; then its removal pushes the set.
    private Task<Guid?> AgentOf(Guid printerId, CancellationToken cancellationToken) =>
        _context.Printers
            .AsNoTracking()
            .Where(p => p.Id == printerId)
            .Select(p => p.AgentId)
            .FirstOrDefaultAsync(cancellationToken);
}
