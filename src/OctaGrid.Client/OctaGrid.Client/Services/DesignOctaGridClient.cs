using OctaGrid.Client.Contracts;
using OctaGrid.Protocol;
using OctaGrid.Protocol.Agents;
using OctaGrid.Protocol.Printers;
using OctaGrid.Protocol.Users;
using OctaGrid.Protocol.Workspaces;

namespace OctaGrid.Client.Services;

// A single mock backing every client interface for the XAML previewer only
internal sealed class DesignOctaGridClient : IUsersClient, IWorkspacesClient, IPrintersClient, IAgentsClient, IAuthClient, IDashboardsClient
{
    public static readonly Guid DefaultDashboardId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    public static readonly Guid DefaultWorkspaceId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public Task AddPrinter(Guid agentId, Guid printerId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<AgentResponse> CreateAgent(Guid workspaceId, string name, CancellationToken cancellationToken) =>
        Task.FromResult(new AgentResponse(Guid.NewGuid(), name, "Online", "octa_dev_token", 1));

    public Task<DashboardResponse> CreateDashboard(Guid workspaceId, CreateDashboardRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new DashboardResponse(Guid.NewGuid(), request.Name, request.Type, 1));

    public Task<PrinterResponse> CreatePrinter(Guid workspaceId, CreatePrinterRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new PrinterResponse(Guid.NewGuid(), request.Name, request.Url, request.ConnectionProtocol, "Connected", request.ApiKey ?? string.Empty, request.Enabled, 1));

    public Task CreateUser(CreateUserRequest request, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<WorkspaceResponse> CreateWorkspace(string name, CancellationToken cancellationToken) =>
        Task.FromResult(new WorkspaceResponse(DefaultWorkspaceId, name, 1));

    public Task DeletePrinter(Guid printerId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<List<AgentResponse>> GetAgentList(Guid workspaceId, CancellationToken cancellationToken) =>
        Task.FromResult<List<AgentResponse>>([
            new AgentResponse(Guid.NewGuid(), "Local Agent", "Online", "octa_local_token", 1)
        ]);

    public Task<List<DashboardResponse>> GetDashboardList(Guid workspaceId, CancellationToken cancellationToken) =>
        Task.FromResult<List<DashboardResponse>>([
            new DashboardResponse(DefaultDashboardId, "Main Dashboard", "List", 1)
        ]);

    public Task<PrinterResponse> GetPrinter(Guid workspaceId, Guid printerId, CancellationToken cancellationToken) =>
        Task.FromResult(new PrinterResponse(printerId, "Voron 2.4", "http://192.168.1.50", "Moonraker", "Connected", string.Empty, true, 1));

    public Task<List<PrinterResponse>> GetPrintersList(Guid workspaceId, GetPrintersQuery query, CancellationToken cancellationToken) =>
        Task.FromResult<List<PrinterResponse>>([
            new PrinterResponse(Guid.NewGuid(), "Voron 2.4", "http://192.168.1.50", "Moonraker", "Connected", string.Empty, true, 1),
            new PrinterResponse(Guid.NewGuid(), "Bambu Lab X1C", "192.168.1.51", "BambuMqtt", "Connected", string.Empty, true, 1)
        ]);

    public Task<List<WorkspaceResponse>> GetWorkspaceList(CancellationToken cancellationToken) =>
        Task.FromResult<List<WorkspaceResponse>>([
            new WorkspaceResponse(DefaultWorkspaceId, "Default Workspace", 1)
        ]);

    public Task<List<WorkspaceSummaryResponse>> GetWorkspacesSummary(CancellationToken cancellationToken) =>
        Task.FromResult<List<WorkspaceSummaryResponse>>([
            new WorkspaceSummaryResponse(DefaultWorkspaceId, "Default Workspace", "Owner", 1, 1, 2)
        ]);

    public Task<UserResponse> Me(CancellationToken cancellationToken) =>
        Task.FromResult(new UserResponse(Guid.NewGuid(), "Design User", "design@octagrid.local"));

    public Task SetPrinterEnabled(Guid printerId, bool state, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task SignIn(SignInRequest request, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<AgentResponse> UpdateAgent(Guid agentId, string name, CancellationToken cancellationToken) =>
        Task.FromResult(new AgentResponse(agentId, name, "Online", "octa_local_token", 1));

    public Task<DashboardResponse> UpdateDashboard(Guid dashboardId, UpdateDashboardRequest request, CancellationToken cancellationToken) =>
         Task.FromResult(new DashboardResponse(Guid.NewGuid(), request.Name, "List", 1));

    public Task<PrinterResponse> UpdateIdentity(Guid printerId, UpdatePrinterIdentityRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new PrinterResponse(printerId, "Voron 2.4", "http://192.168.1.50", "Moonraker", "Connected", string.Empty, true, 1));

    public Task<PrinterResponse> UpdatePrinter(Guid printerId, UpdatePrinterRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new PrinterResponse(printerId, request.Name ?? string.Empty, request.Url ?? string.Empty, request.ConnectionProtocol ?? string.Empty, "Connected", request.ApiKey ?? string.Empty, request.Enabled, 1));

    public Task<WorkspaceResponse> UpdateWorkspace(Guid workspaceId, string name, CancellationToken cancellationToken) =>
        Task.FromResult(new WorkspaceResponse(workspaceId, name, 1));
}
