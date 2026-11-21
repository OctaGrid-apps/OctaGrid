using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Application.Authorization;
using OctaGrid.Application.Validation;
using OctaGrid.Data;
using OctaGrid.Data.Models;
using OctaGrid.Extensions.Models.Exceptions;
using OctaGrid.Protocol.Dashboards;
using OctaGrid.Protocol.Permissions;

namespace OctaGrid.Application.Services;

/// <summary>
/// A workspace's dashboards. Adding, removing and choosing the default go through the <see cref="Workspace"/>, which
/// keeps the rules about the whole list; renaming touches only the dashboard.
/// </summary>
public sealed class DashboardsService
{
    private readonly WorkspaceAccess _access;
    private readonly OctaGridContext _context;
    private readonly ILogger<DashboardsService> _logger;

    public DashboardsService(OctaGridContext context, WorkspaceAccess access, ILogger<DashboardsService>? logger = null)
    {
        _context = context;
        _access = access;
        _logger = logger ?? NullLogger<DashboardsService>.Instance;
    }

    public async Task<DashboardResponse> Create(Guid userId, Guid workspaceId, CreateDashboardRequest request, CancellationToken cancellationToken)
    {
        await _access.Require(userId, workspaceId, Permission.ManageDashboards, cancellationToken);

        var results = new CreateDashboardRequestValidator().Validate(request);
        if (!results.IsValid)
        {
            var failures = results.Errors.ToDictionary(x => x.PropertyName, x => x.ErrorMessage).AsReadOnly();
            throw new ValidationException(failures);
        }

        var workspace = await LoadWorkspace(workspaceId, cancellationToken);
        var dashboard = workspace.AddDashboard(request.Name, request.Type);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created dashboard {DashboardId} ('{DashboardName}', type {DashboardType}) in workspace {WorkspaceId} by user {UserId}",
            dashboard.Id, dashboard.Name, dashboard.Type, workspaceId, userId);

        return ToResponse(dashboard);
    }

    public async Task<List<DashboardResponse>> GetList(Guid userId, Guid workspaceId, CancellationToken cancellationToken)
    {
        await _access.Require(userId, workspaceId, Permission.View, cancellationToken);

        var dashboards = await _context.Dashboards
            .AsNoTracking()
            .Where(d => d.WorkspaceId == workspaceId)
            .ToListAsync(cancellationToken);

        // Sorted in memory so the order is the same for every database provider (SQLite compares text case-sensitively).
        return dashboards
            .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(ToResponse)
            .ToList();
    }

    public async Task<DashboardResponse> Update(Guid userId, Guid dashboardId, UpdateDashboardRequest request, CancellationToken cancellationToken)
    {
        var dashboard = await _context.Dashboards.FindAsync([dashboardId], cancellationToken)
            ?? throw new NotFoundException(nameof(Dashboard), dashboardId);
        await _access.Require(userId, dashboard.WorkspaceId, Permission.ManageDashboards, cancellationToken);

        var validator = new UpdateDashboardRequestValidator();
        var results = validator.Validate(request);
        if (!results.IsValid)
        {
            var failures = results.Errors.ToDictionary(x => x.PropertyName, x => x.ErrorMessage).AsReadOnly();
            throw new ValidationException(failures);
        }

        dashboard.Rename(request.Name);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated dashboard {DashboardId} ('{Name}', IsDefault={IsDefault}) in workspace {WorkspaceId} by user {UserId}",
            dashboard.Id, dashboard.Name, dashboard.IsDefault, dashboard.WorkspaceId, userId);

        return ToResponse(dashboard);
    }

    public async Task<DashboardResponse> SetDefault(Guid userId, Guid dashboardId, CancellationToken cancellationToken)
    {
        var workspace = await LoadWorkspaceOf(userId, dashboardId, cancellationToken);
        var dashboard = workspace.Dashboards.Single(d => d.Id == dashboardId);
        if (!dashboard.IsDefault)
        {
            workspace.SetDefaultDashboard(dashboardId);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Dashboard {DashboardId} ('{Name}') is now the default of workspace {WorkspaceId} (user {UserId})",
                dashboard.Id, dashboard.Name, dashboard.WorkspaceId, userId);
        }

        return ToResponse(dashboard);
    }

    public async Task Delete(Guid userId, Guid dashboardId, CancellationToken cancellationToken)
    {
        var workspace = await LoadWorkspaceOf(userId, dashboardId, cancellationToken);
        var dashboard = workspace.RemoveDashboard(dashboardId);

        // Its widgets go with it (cascade in the database).
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted dashboard {DashboardId} ('{Name}') from workspace {WorkspaceId} by user {UserId}",
            dashboard.Id, dashboard.Name, dashboard.WorkspaceId, userId);
    }

    private async Task<Workspace> LoadWorkspace(Guid workspaceId, CancellationToken cancellationToken) =>
        await _context.Workspaces
            .Include(w => w.Dashboards)
            .FirstOrDefaultAsync(w => w.Id == workspaceId, cancellationToken)
        ?? throw new NotFoundException(nameof(Workspace), workspaceId);

    // Also the authorization: it fails unless the user may manage the dashboards of the dashboard's workspace.
    private async Task<Workspace> LoadWorkspaceOf(Guid userId, Guid dashboardId, CancellationToken cancellationToken)
    {
        var workspaceId = await _context.Dashboards
            .AsNoTracking()
            .Where(d => d.Id == dashboardId)
            .Select(d => (Guid?)d.WorkspaceId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(Dashboard), dashboardId);
        await _access.Require(userId, workspaceId, Permission.ManageDashboards, cancellationToken);
        return await LoadWorkspace(workspaceId, cancellationToken);
    }

    private static DashboardResponse ToResponse(Dashboard dashboard) =>
        new(dashboard.Id, dashboard.Name, dashboard.Type, dashboard.IsDefault, dashboard.Version);
}
