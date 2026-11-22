using Microsoft.EntityFrameworkCore;
using OctaGrid.Application.Authorization;
using OctaGrid.Data;
using OctaGrid.Data.Models;
using OctaGrid.Extensions.Models.Exceptions;
using OctaGrid.Protocol.Permissions;

namespace OctaGrid.Application.Agents;

/// <summary>
/// Resolves the agent that handles a user's request about a printer (<see cref="Printer.AgentId"/>)
/// and checks that the user has the required permission in the printer's workspace.
/// </summary>
public sealed class PrinterRouting
{
    private readonly WorkspaceAccess _access;
    private readonly OctaGridContext _context;

    public PrinterRouting(OctaGridContext context, WorkspaceAccess access)
    {
        _context = context;
        _access = access;
    }

    /// <summary>Returns the agent of the printer.</summary>
    /// <exception cref="NotFoundException">There is no such printer.</exception>
    /// <exception cref="AuthenticationException">The user is not a member of the printer's workspace.</exception>
    /// <exception cref="ForbiddenException">The user lacks <paramref name="permission"/> there.</exception>
    /// <exception cref="ValidationException">The printer has no agent.</exception>
    public async Task<Guid> AgentOf(Guid userId, Guid printerId, Permission permission, CancellationToken cancellationToken) =>
        await AssignedAgentOf(userId, printerId, permission, cancellationToken)
        ?? throw new ValidationException(nameof(Printer.AgentId), "The printer has no agent to connect to it.");

    /// <summary>
    /// Returns the agent of the printer, or null when the printer has none.
    /// Throws the same exceptions as <see cref="AgentOf"/> otherwise.
    /// </summary>
    public async Task<Guid?> AssignedAgentOf(Guid userId, Guid printerId, Permission permission, CancellationToken cancellationToken)
    {
        var printer = await _context.Printers
            .AsNoTracking()
            .Where(p => p.Id == printerId)
            .Select(p => new { p.WorkspaceId, p.AgentId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(Printer), printerId);

        await _access.Require(userId, printer.WorkspaceId, permission, cancellationToken);
        return printer.AgentId;
    }

    /// <summary>
    /// Returns the agent of each printer, keyed by printer id. Unlike <see cref="AgentOf"/>, it does not throw:
    /// printers that do not exist, have no agent, or lack <paramref name="permission"/> are omitted,
    /// so one inaccessible printer does not fail the whole list.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, Guid>> AllowedAgentsOf(
        Guid userId, IReadOnlyCollection<Guid> printerIds, Permission permission, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(printerIds);

        var printers = await _context.Printers
            .AsNoTracking()
            .Where(p => printerIds.Contains(p.Id) && p.AgentId != null)
            .Select(p => new { p.Id, p.WorkspaceId, AgentId = p.AgentId!.Value })
            .ToListAsync(cancellationToken);

        var allowed = new HashSet<Guid>();
        foreach (var workspaceId in printers.Select(p => p.WorkspaceId).Distinct())
        {
            if ((await _access.GetPermissions(userId, workspaceId, cancellationToken)).Contains(permission))
            {
                allowed.Add(workspaceId);
            }
        }

        return printers.Where(p => allowed.Contains(p.WorkspaceId)).ToDictionary(p => p.Id, p => p.AgentId);
    }
}
