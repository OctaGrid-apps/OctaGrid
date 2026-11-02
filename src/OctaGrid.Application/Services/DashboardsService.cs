using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Application.Validation;
using OctaGrid.Data.Abstractions;
using OctaGrid.Data.Abstractions.Models;
using OctaGrid.Protocol.Exceptions;
using OctaGrid.Protocol.Workspaces;

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

        var isFirst = !await _context.Dashboards.AnyAsync(d => d.WorkspaceId == workspaceId, cancellationToken);

        var dashboard = new Dashboard()
        {
            Name = request.Name,
            Type = request.Type.ParseOrThrow<DashboardType>(),
            WorkspaceId = workspaceId,
            IsDefault = isFirst
        };

        await _context.Dashboards.AddAsync(dashboard, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created dashboard {DashboardId} ('{DashboardName}', type {DashboardType}) in workspace {WorkspaceId} by user {UserId}",
            dashboard.Id, dashboard.Name, dashboard.Type, workspaceId, userId);

        return new DashboardResponse(dashboard.Id, dashboard.Name, dashboard.Type.ToString(), dashboard.Version);
    }

    public async Task<List<DashboardResponse>> GetList(Guid userId, Guid workspaceId, CancellationToken cancellationToken)
    {
        await _context.WorkspaceMembers.ValidateUserAccess(userId, workspaceId, cancellationToken);

        var response = await _context.Dashboards
            .AsNoTracking()
            .Where(d => d.WorkspaceId == workspaceId)
            .Select(d => new DashboardResponse(d.Id, d.Name, d.Type.ToString(), d.Version))
            .ToListAsync(cancellationToken);

        return response;
    }

    public async Task<DashboardResponse> Update(Guid userId, Guid dashboardId, UpdateDashboardRequest request, CancellationToken cancellationToken)
    {
        var dashboard = await _context.Dashboards.FindAsync([dashboardId], cancellationToken)
            ?? throw new NotFoundException(nameof(Dashboard), dashboardId);

        await _context.WorkspaceMembers.ValidateUserAccess(userId, dashboard.WorkspaceId, cancellationToken);

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

        return new DashboardResponse(dashboard.Id, dashboard.Name, dashboard.Type.ToString(), dashboard.Version);
    }
}
