using OctaGrid.Protocol.Dashboards;

namespace OctaGrid.Client.Contracts;

public interface IDashboardsClient
{
    Task<DashboardResponse> CreateDashboard(Guid workspaceId, CreateDashboardRequest request, CancellationToken cancellationToken);

    /// <summary>Deletes a dashboard. The last dashboard of a workspace cannot be deleted.</summary>
    Task DeleteDashboard(Guid dashboardId, CancellationToken cancellationToken);

    Task<List<DashboardResponse>> GetDashboardList(Guid workspaceId, CancellationToken cancellationToken);

    /// <summary>Makes the dashboard the one the workspace opens first.</summary>
    Task<DashboardResponse> SetDefaultDashboard(Guid dashboardId, CancellationToken cancellationToken);

    Task<DashboardResponse> UpdateDashboard(Guid dashboardId, UpdateDashboardRequest request, CancellationToken cancellationToken);
}
