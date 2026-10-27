using OctaGrid.Protocol.Workspaces;

namespace OctaGrid.Client.Contracts;

public interface IWorkspacesClient
{
    Task<WorkspaceResponse> CreateWorkspace(string name, CancellationToken cancellationToken);

    Task<List<WorkspaceResponse>> GetWorkspaceList(CancellationToken cancellationToken);

    Task<List<WorkspaceSummaryResponse>> GetWorkspacesSummary(CancellationToken cancellationToken);

    Task<WorkspaceResponse> UpdateWorkspace(Guid workspaceId, string name, CancellationToken cancellationToken);
}
