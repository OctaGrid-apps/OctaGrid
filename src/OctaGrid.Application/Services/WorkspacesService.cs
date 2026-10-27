using Microsoft.EntityFrameworkCore;
using OctaGrid.Application.Telemetry;
using OctaGrid.Data.Abstractions;
using OctaGrid.Data.Abstractions.Models;
using OctaGrid.Protocol.Exceptions;
using OctaGrid.Protocol.Messaging;
using OctaGrid.Protocol.Workspaces;

namespace OctaGrid.Application.Services;

public sealed class WorkspacesService
{
    private readonly OctaGridContext _context;
    private readonly IMessagePublisher _publisher;

    public WorkspacesService(OctaGridContext context, IMessagePublisher publisher)
    {
        _context = context;
        _publisher = publisher;
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

        await _context.Workspaces.AddAsync(workspace, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return new WorkspaceResponse(workspace.Id, workspace.Name, workspace.Version);
    }

    public async Task<List<WorkspaceResponse>> GetList(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _context.Users.FindAsync([userId], cancellationToken)
            ?? throw new AuthenticationException("User is not found");

        var response = await _context.WorkspaceMembers
            .AsNoTracking()
            .Where(uw => uw.UserId == userId)
            .Select(uw => new WorkspaceResponse(uw.Workspace!.Id, uw.Workspace.Name, uw.Version))
            .ToListAsync(cancellationToken);

        return response;
    }

    public async Task<List<WorkspaceSummaryResponse>> GetSummary(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _context.Users.FindAsync([userId], cancellationToken)
            ?? throw new AuthenticationException("User is not found");

        var response = await _context.WorkspaceMembers
            .AsNoTracking()
            .Where(uw => uw.UserId == userId)
            .Select(uw => new WorkspaceSummaryResponse(
                uw.WorkspaceId,
                uw.Workspace!.Name,
                uw.Role.ToString(),
                uw.Workspace.Members.Count(),
                uw.Workspace.Agents.Count(),
                uw.Workspace.Printers.Count()
            ))
            .ToListAsync(cancellationToken);

        return response;
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

        var userIsWorkspaceMember = await _context.WorkspaceMembers.AnyAsync(wm => wm.UserId == userId && wm.WorkspaceId == workspaceId, cancellationToken);
        if (!userIsWorkspaceMember)
        {
            throw new AuthenticationException("User does not have access to this workspace");
        }
        workspace.Name = request.Name;

        await _context.SaveChangesAsync(cancellationToken);
        _publisher.Publish(new EntityChanged(EntityTypes.Workspace, workspace.Id, workspace.Version, ChangeKind.Upserted, workspace.Id));

        return new WorkspaceResponse(workspace.Id, workspace.Name, workspace.Version);
    }
}
