using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Application.Agents;
using OctaGrid.Application.Authorization;
using OctaGrid.Application.Validation;
using OctaGrid.Data;
using OctaGrid.Data.Models;
using OctaGrid.Extensions.Models.Exceptions;
using OctaGrid.Protocol.Agents;
using OctaGrid.Protocol.Permissions;

namespace OctaGrid.Application.Services;

public sealed class AgentsService
{
    private readonly WorkspaceAccess _access;
    private readonly OctaGridContext _context;
    private readonly IAgentLink _link;
    private readonly ILogger<AgentsService> _logger;

    public AgentsService(OctaGridContext context, WorkspaceAccess access, IAgentLink link, ILogger<AgentsService>? logger = null)
    {
        _context = context;
        _access = access;
        _link = link;
        _logger = logger ?? NullLogger<AgentsService>.Instance;
    }

    /// <summary>
    /// Makes an agent the one that connects to a printer, replacing the printer's previous agent. The agent and
    /// the printer must be in the same workspace.
    /// </summary>
    public async Task AssignPrinter(Guid userId, Guid agentId, Guid printerId, CancellationToken cancellationToken)
    {
        var agent = await _context.Agents.FindAsync([agentId], cancellationToken)
            ?? throw new NotFoundException(nameof(Agent), agentId);

        await _access.Require(userId, agent.WorkspaceId, Permission.ManagePrinters, cancellationToken);

        var printer = await _context.Printers.FindAsync([printerId], cancellationToken)
            ?? throw new NotFoundException(nameof(Printer), printerId);

        if (printer.AgentId == agentId)
        {
            return;
        }

        var previousAgentId = printer.AgentId;
        // Rejects an agent of another workspace (ValidationException).
        printer.AssignTo(agent);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Assigned printer {PrinterId} to agent {AgentId} (was {PreviousAgentId}) in workspace {WorkspaceId} by user {UserId}",
            printerId, agentId, previousAgentId, agent.WorkspaceId, userId);
    }

    public async Task<AgentResponse> Create(Guid userId, Guid workspaceId, CreateAgentRequest request, CancellationToken cancellationToken)
    {
        await _access.Require(userId, workspaceId, Permission.ManageAgents, cancellationToken);

        Agent agent = new()
        {
            WorkspaceId = workspaceId,
            Name = request.Name,
            Status = new AgentStatus()
            {
                State = AgentState.PendingEnrollment
            }
        };

        _context.Agents.Add(agent);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created agent {AgentId} ('{AgentName}') in workspace {WorkspaceId} by user {UserId}",
            agent.Id, agent.Name, workspaceId, userId);

        return new AgentResponse(agent.Id, agent.Name, agent.Status.State.ToString(), agent.TokenPrefix, agent.Version)
        {
            Connection = _link.GetConnectionState(agent.Id),
        };
    }

    public async Task<List<AgentResponse>> GetList(Guid userId, Guid workspaceId, CancellationToken cancellationToken)
    {
        await _access.Require(userId, workspaceId, Permission.View, cancellationToken);

        var agents = await _context.Agents
            .AsNoTracking()
            .Where(a => a.WorkspaceId == workspaceId)
            // Ids are version 7 GUIDs, so they sort by creation time; SQLite cannot sort DateTimeOffset columns.
            .OrderBy(a => a.Id)
            .Select(a => new AgentResponse(a.Id, a.Name, a.Status != null ? a.Status.State.ToString() : nameof(AgentState.Unknown), a.TokenPrefix, a.Version))
            .ToListAsync(cancellationToken);

        // Whether an agent is connected is known to the link, not stored.
        return agents.Select(a => a with { Connection = _link.GetConnectionState(a.Id) }).ToList();
    }

    public async Task<AgentResponse> Update(Guid userId, Guid agentId, UpdateAgentRequest request, CancellationToken cancellationToken)
    {
        var validator = new UpdateAgentRequestValidator();
        var results = validator.Validate(request);

        if (!results.IsValid)
        {
            var failures = results.Errors.ToDictionary(x => x.PropertyName, x => x.ErrorMessage).AsReadOnly();
            throw new ValidationException(failures);
        }

        var agent = await _context.Agents.Include(x => x.Status).FirstOrDefaultAsync(a => a.Id == agentId, cancellationToken)
            ?? throw new NotFoundException(nameof(Agent), agentId);

        await _access.Require(userId, agent.WorkspaceId, Permission.ManageAgents, cancellationToken);

        agent.Name = request.Name;
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated agent {AgentId} ('{AgentName}') in workspace {WorkspaceId} by user {UserId}",
            agent.Id, agent.Name, agent.WorkspaceId, userId);

        var state = agent.Status is null ? nameof(AgentState.Unknown) : agent.Status.State.ToString();
        return new AgentResponse(agent.Id, agent.Name, state, agent.TokenPrefix, agent.Version)
        {
            Connection = _link.GetConnectionState(agent.Id),
        };
    }
}
