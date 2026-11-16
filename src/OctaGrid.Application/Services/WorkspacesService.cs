using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Application.Authorization;
using OctaGrid.Application.Telemetry;
using OctaGrid.Data;
using OctaGrid.Data.Models;
using OctaGrid.Extensions.Models.Exceptions;
using OctaGrid.Protocol.Dashboards;
using OctaGrid.Protocol.Messaging;
using OctaGrid.Protocol.Permissions;
using OctaGrid.Protocol.Workspaces;

namespace OctaGrid.Application.Services;

public sealed class WorkspacesService
{
    private readonly WorkspaceAccess _access;
    private readonly OctaGridContext _context;
    private readonly ILogger<WorkspacesService> _logger;
    private readonly IMessagePublisher _publisher;

    public WorkspacesService(OctaGridContext context, WorkspaceAccess access, IMessagePublisher publisher, ILogger<WorkspacesService>? logger = null)
    {
        _context = context;
        _access = access;
        _publisher = publisher;
        _logger = logger ?? NullLogger<WorkspacesService>.Instance;
    }

    public async Task<WorkspaceResponse> Create(Guid userId, CreateWorkspaceRequest request, CancellationToken cancellationToken)
    {
        var userExists = await _context.Users
            .AnyAsync(u => u.Id == userId, cancellationToken);

        if (!userExists)
        {
            throw new AuthenticationException("User is not found");
        }

        var workspace = new Workspace()
        {
            Name = request.Name
        };

        workspace.Members.Add(new WorkspaceMember()
        {
            UserId = userId,
            Role = Role.Owner,
            JoinedAt = DateTimeOffset.UtcNow
        });

        workspace.Dashboards.Add(new Dashboard()
        {
            Name = "Default",
            Type = DashboardType.List,
            IsDefault = true
        });

        await _context.Workspaces.AddAsync(workspace, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created workspace {WorkspaceId} ('{WorkspaceName}') for owner {UserId}",
            workspace.Id, workspace.Name, userId);

        return new WorkspaceResponse(workspace.Id, workspace.Name, workspace.Version, _access.PermissionsOf(Role.Owner));
    }

    public async Task<List<WorkspaceResponse>> GetList(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _context.Users.FindAsync([userId], cancellationToken)
            ?? throw new AuthenticationException("User is not found");

        var memberships = await _context.WorkspaceMembers
            .AsNoTracking()
            .Where(uw => uw.UserId == userId)
            .Select(uw => new { uw.Workspace!.Id, uw.Workspace.Name, uw.Version, uw.Role })
            .ToListAsync(cancellationToken);

        // The role stays here: the client is told what the user may do, not which role that comes from.
        return memberships
            .Select(m => new WorkspaceResponse(m.Id, m.Name, m.Version, _access.PermissionsOf(m.Role)))
            .ToList();
    }

    public async Task<List<WorkspaceSummaryResponse>> GetSummary(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _context.Users.FindAsync([userId], cancellationToken)
            ?? throw new AuthenticationException("User is not found");

        var memberships = await _context.WorkspaceMembers
            .AsNoTracking()
            .Where(uw => uw.UserId == userId)
            .Select(uw => new
            {
                uw.WorkspaceId,
                uw.Workspace!.Name,
                uw.Role,
                Users = uw.Workspace.Members.Count(),
                Agents = uw.Workspace.Agents.Count(),
                Printers = uw.Workspace.Printers.Count()
            })
            .ToListAsync(cancellationToken);

        return memberships
            .Select(m => new WorkspaceSummaryResponse(
                m.WorkspaceId, m.Name, m.Role.ToString(), m.Users, m.Agents, m.Printers, _access.PermissionsOf(m.Role)))
            .ToList();
    }

    public async Task<WorkspaceResponse> Update(Guid userId, Guid workspaceId, UpdateWorkspaceRequest request, CancellationToken cancellationToken)
    {
        var userExists = await _context.Users
            .AnyAsync(u => u.Id == userId, cancellationToken);

        if (!userExists)
        {
            throw new AuthenticationException("User is not found");
        }

        var workspace = await _context.Workspaces.FindAsync([workspaceId], cancellationToken)
            ?? throw new NotFoundException(nameof(Workspace), workspaceId);

        await _access.Require(userId, workspaceId, Permission.ManageWorkspace, cancellationToken);

        workspace.Name = request.Name;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated workspace {WorkspaceId} name to '{WorkspaceName}' by user {UserId}",
            workspace.Id, workspace.Name, userId);

        _publisher.Publish(new EntityChanged(EntityTypes.Workspace, workspace.Id, workspace.Version, ChangeKind.Upserted, workspace.Id));

        return new WorkspaceResponse(
            workspace.Id, workspace.Name, workspace.Version, await _access.GetPermissions(userId, workspaceId, cancellationToken));
    }
}
