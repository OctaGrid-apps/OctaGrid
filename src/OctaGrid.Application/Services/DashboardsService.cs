using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Application.Validation;
using OctaGrid.Data;
using OctaGrid.Data.Models;
using OctaGrid.Protocol.Dashboards;
using OctaGrid.Protocol.Exceptions;

namespace OctaGrid.Application.Services;

public sealed class DashboardsService
{
    private readonly OctaGridContext _context;
    private readonly ILogger<DashboardsService> _logger;

    public DashboardsService(OctaGridContext context, ILogger<DashboardsService>? logger = null)
    {
        _context = context;
        _logger = logger ?? NullLogger<DashboardsService>.Instance;
    }

    public async Task<DashboardResponse> Create(Guid userId, Guid workspaceId, CreateDashboardRequest request, CancellationToken cancellationToken)
    {
        await _context.WorkspaceMembers.ValidateUserAccess(userId, workspaceId, cancellationToken);

        if (!Enum.IsDefined(request.Type))
        {
            throw new ValidationException(nameof(request.Type), $"Unknown dashboard type '{request.Type}'.");
        }

        var isFirst = !await _context.Dashboards.AnyAsync(d => d.WorkspaceId == workspaceId, cancellationToken);

        var dashboard = new Dashboard()
        {
            Name = request.Name,
            Type = request.Type,
            WorkspaceId = workspaceId,
            IsDefault = isFirst
        };

        await _context.Dashboards.AddAsync(dashboard, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created dashboard {DashboardId} ('{DashboardName}', type {DashboardType}) in workspace {WorkspaceId} by user {UserId}",
            dashboard.Id, dashboard.Name, dashboard.Type, workspaceId, userId);

        return ToResponse(dashboard);
    }

    public async Task<List<DashboardResponse>> GetList(Guid userId, Guid workspaceId, CancellationToken cancellationToken)
    {
        await _context.WorkspaceMembers.ValidateUserAccess(userId, workspaceId, cancellationToken);

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
        var dashboard = await FindWithAccess(userId, dashboardId, cancellationToken);

        var validator = new UpdateDashboardRequestValidator();
        var results = validator.Validate(request);

        if (!results.IsValid)
        {
            var failures = results.Errors.ToDictionary(x => x.PropertyName, x => x.ErrorMessage).AsReadOnly();
            throw new ValidationException(failures);
        }

        dashboard.Name = request.Name;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated dashboard {DashboardId} ('{Name}', IsDefault={IsDefault}) in workspace {WorkspaceId} by user {UserId}",
            dashboard.Id, dashboard.Name, dashboard.IsDefault, dashboard.WorkspaceId, userId);

        return ToResponse(dashboard);
    }

    /// <summary>Makes the dashboard the one the workspace opens first; the previous default stops being one.</summary>
    public async Task<DashboardResponse> SetDefault(Guid userId, Guid dashboardId, CancellationToken cancellationToken)
    {
        var dashboard = await FindWithAccess(userId, dashboardId, cancellationToken);

        if (!dashboard.IsDefault)
        {
            var others = await _context.Dashboards
                .Where(d => d.WorkspaceId == dashboard.WorkspaceId && d.IsDefault && d.Id != dashboard.Id)
                .ToListAsync(cancellationToken);
            foreach (var other in others)
            {
                other.IsDefault = false;
            }

            dashboard.IsDefault = true;
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Dashboard {DashboardId} ('{Name}') is now the default of workspace {WorkspaceId} (user {UserId})",
                dashboard.Id, dashboard.Name, dashboard.WorkspaceId, userId);
        }

        return ToResponse(dashboard);
    }

    /// <summary>
    /// Deletes a dashboard with its widgets. The last dashboard of a workspace cannot be deleted; when the default one
    /// is deleted, the first remaining dashboard by name becomes the default.
    /// </summary>
    public async Task Delete(Guid userId, Guid dashboardId, CancellationToken cancellationToken)
    {
        var dashboard = await FindWithAccess(userId, dashboardId, cancellationToken);

        var remaining = await _context.Dashboards
            .Where(d => d.WorkspaceId == dashboard.WorkspaceId && d.Id != dashboard.Id)
            .ToListAsync(cancellationToken);

        if (remaining.Count == 0)
        {
            throw new ValidationException(nameof(dashboardId), "A workspace needs at least one dashboard, so the last one cannot be deleted.");
        }

        if (dashboard.IsDefault)
        {
            remaining.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).First().IsDefault = true;
        }

        var widgets = await _context.DashboardWidgets.Where(w => w.DashboardId == dashboard.Id).ToListAsync(cancellationToken);
        _context.DashboardWidgets.RemoveRange(widgets);
        _context.Dashboards.Remove(dashboard);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted dashboard {DashboardId} ('{Name}') with {WidgetCount} widget(s) from workspace {WorkspaceId} by user {UserId}",
            dashboard.Id, dashboard.Name, widgets.Count, dashboard.WorkspaceId, userId);
    }

    private async Task<Dashboard> FindWithAccess(Guid userId, Guid dashboardId, CancellationToken cancellationToken)
    {
        var dashboard = await _context.Dashboards.FindAsync([dashboardId], cancellationToken)
            ?? throw new NotFoundException(nameof(Dashboard), dashboardId);

        await _context.WorkspaceMembers.ValidateUserAccess(userId, dashboard.WorkspaceId, cancellationToken);
        return dashboard;
    }

    private static DashboardResponse ToResponse(Dashboard dashboard) =>
        new(dashboard.Id, dashboard.Name, dashboard.Type, dashboard.IsDefault, dashboard.Version);
}
