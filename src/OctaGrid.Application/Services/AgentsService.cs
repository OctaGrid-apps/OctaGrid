using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Application.Validation;
using OctaGrid.Data;
using OctaGrid.Data.Models;
using OctaGrid.Extensions.Models.Exceptions;
using OctaGrid.Protocol.Agents;

namespace OctaGrid.Application.Services;

public sealed class AgentsService
{
    private readonly OctaGridContext _context;
    private readonly ILogger<AgentsService> _logger;

    public AgentsService(OctaGridContext context, ILogger<AgentsService>? logger = null)
    {
        _context = context;
        _logger = logger ?? NullLogger<AgentsService>.Instance;
    }

    public async Task AddPrinter(Guid userId, Guid agentId, Guid printerId, CancellationToken cancellationToken)
    {
        var agent = await _context.Agents.FindAsync([agentId], cancellationToken)
            ?? throw new NotFoundException(nameof(Agent), agentId);

        await _context.WorkspaceMembers.ValidateUserAccess(userId, agent.WorkspaceId, cancellationToken);

        var printer = await _context.Printers.FindAsync([printerId], cancellationToken)
            ?? throw new NotFoundException(nameof(Printer), printerId);

        if (agent.WorkspaceId != printer.WorkspaceId)
        {
            _logger.LogWarning("Rejected linking printer {PrinterId} (workspace {PrinterWorkspaceId}) to agent {AgentId} (workspace {AgentWorkspaceId})",
                printerId, printer.WorkspaceId, agentId, agent.WorkspaceId);
            throw new ValidationException(nameof(Agent), "Printer and agent should be in the same workspace");
        }

        PrinterAgent printerAgent = new()
        {
            Printer = printer,
            Agent = agent
        };

        _context.PrinterAgents.Add(printerAgent);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Assigned printer {PrinterId} to agent {AgentId} in workspace {WorkspaceId} by user {UserId}",
            printerId, agentId, agent.WorkspaceId, userId);
    }

    public async Task<AgentResponse> Create(Guid userId, Guid workspaceId, CreateAgentRequest request, CancellationToken cancellationToken)
    {
        await _context.WorkspaceMembers.ValidateUserAccess(userId, workspaceId, cancellationToken);

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

        return new AgentResponse(agent.Id, agent.Name, agent.Status.State.ToString(), agent.TokenPrefix, agent.Version);
    }

    public async Task<List<AgentResponse>> GetList(Guid userId, Guid workspaceId, CancellationToken cancellationToken)
    {
        await _context.WorkspaceMembers.ValidateUserAccess(userId, workspaceId, cancellationToken);

        var agents = await _context.Agents
            .Where(a => a.WorkspaceId == workspaceId)
            .Include(x => x.Status)
            .Select(a => new AgentResponse(a.Id, a.Name, a.Status != null ? a.Status.State.ToString() : nameof(AgentState.Unknown), a.TokenPrefix, a.Version))
            .ToListAsync(cancellationToken);

        return agents;
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

        await _context.WorkspaceMembers.ValidateUserAccess(userId, agent.WorkspaceId, cancellationToken);

        agent.Name = request.Name;
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated agent {AgentId} ('{AgentName}') in workspace {WorkspaceId} by user {UserId}",
            agent.Id, agent.Name, agent.WorkspaceId, userId);

        var state = agent.Status is null ? nameof(AgentState.Unknown) : agent.Status.State.ToString();
        return new AgentResponse(agent.Id, agent.Name, state, agent.TokenPrefix, agent.Version);
    }
}
