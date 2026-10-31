using OctaGrid.Protocol.Workspaces;

namespace OctaGrid.Client.Contracts;

public interface IDashboardsClient
{
    Task<DashboardResponse> CreateDashboard(Guid workspaceId, CreateDashboardRequest request, CancellationToken cancellationToken);

    Task<List<DashboardResponse>> GetDashboardList(Guid workspaceId, CancellationToken cancellationToken);

    Task<DashboardResponse> UpdateDashboard(Guid dashboardId, UpdateDashboardRequest request, CancellationToken cancellationToken);
}
