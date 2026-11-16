using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Data;
using OctaGrid.Data.Models;
using OctaGrid.Extensions.Models.Exceptions;
using OctaGrid.Protocol.Permissions;

namespace OctaGrid.Application.Authorization;

/// <summary>
/// The check every service makes before it acts for a user: is the user a member of the workspace, and does the
/// member's role have the permission the action needs (<see cref="IPermissionPolicy"/>).
/// </summary>
public sealed class WorkspaceAccess
{
    private readonly OctaGridContext _context;
    private readonly ILogger<WorkspaceAccess> _logger;
    private readonly IPermissionPolicy _policy;

    public WorkspaceAccess(OctaGridContext context, IPermissionPolicy policy, ILogger<WorkspaceAccess>? logger = null)
    {
        _context = context;
        _policy = policy;
        _logger = logger ?? NullLogger<WorkspaceAccess>.Instance;
    }

    /// <summary>
    /// Fails with <see cref="AuthenticationException"/> when the user is not a member of the workspace, and with
    /// <see cref="ForbiddenException"/> when the member's role lacks the permission.
    /// </summary>
    public async Task Require(Guid userId, Guid workspaceId, Permission permission, CancellationToken cancellationToken)
    {
        var role = await _context.WorkspaceMembers
            .AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId && m.UserId == userId)
            .Select(m => (Role?)m.Role)
            .FirstOrDefaultAsync(cancellationToken);

        if (role is null)
        {
            _logger.LogWarning("User {UserId} is not a member of workspace {WorkspaceId}", userId, workspaceId);
            throw new AuthenticationException("User does not have access to this workspace");
        }

        if (!_policy.For(role.Value).Contains(permission))
        {
            _logger.LogWarning("User {UserId} ({Role}) was refused {Permission} in workspace {WorkspaceId}", userId, role.Value, permission, workspaceId);
            throw new ForbiddenException();
        }
    }

    /// <summary>The same check for an agent: fails when the agent does not exist, then checks its workspace.</summary>
    public async Task RequireForAgent(Guid userId, Guid agentId, Permission permission, CancellationToken cancellationToken)
    {
        var workspaceId = await _context.Agents
            .AsNoTracking()
            .Where(a => a.Id == agentId)
            .Select(a => (Guid?)a.WorkspaceId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(Agent), agentId);

        await Require(userId, workspaceId, permission, cancellationToken);
    }
}
