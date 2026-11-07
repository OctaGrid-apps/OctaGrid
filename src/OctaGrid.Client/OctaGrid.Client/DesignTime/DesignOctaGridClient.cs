using OctaGrid.Client.Contracts;
using OctaGrid.Protocol;
using OctaGrid.Protocol.Agents;
using OctaGrid.Protocol.Printers;
using OctaGrid.Protocol.Users;
using OctaGrid.Protocol.Workspaces;

namespace OctaGrid.Client.DesignTime;

/// <summary>
/// One in-memory client that backs every client interface with sample data.
/// Used by the XAML designer (through <see cref="DesignData"/>) and by tests; never by the running app.
/// </summary>
internal sealed class DesignOctaGridClient : IUsersClient, IWorkspacesClient, IPrintersClient, IAgentsClient, IAuthClient, IDashboardsClient
{
    public static readonly Guid DefaultDashboardId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    public static readonly Guid DefaultWorkspaceId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public static readonly Guid VoronPrinterId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    public static readonly Guid BambuPrinterId = Guid.Parse("00000000-0000-0000-0000-0000000000a2");

    private static readonly IReadOnlyDictionary<string, string> NoSecrets = new Dictionary<string, string>();
    private static readonly IReadOnlyDictionary<string, string> VoronSettings = new Dictionary<string, string> { ["url"] = "192.168.1.50:7125" };

    public Task AddPrinter(Guid agentId, Guid printerId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<AgentResponse> CreateAgent(Guid workspaceId, string name, CancellationToken cancellationToken) =>
        Task.FromResult(new AgentResponse(Guid.NewGuid(), name, "Online", "octa_dev_token", 1));

    public Task<DashboardResponse> CreateDashboard(Guid workspaceId, CreateDashboardRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new DashboardResponse(Guid.NewGuid(), request.Name, request.Type, false, 1));

    public Task<PrinterResponse> CreatePrinter(Guid workspaceId, CreatePrinterRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new PrinterResponse(Guid.NewGuid(), request.Name, request.ConnectorId, request.Settings, request.Secrets, "Connected", request.Enabled, 1));

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
            new DashboardResponse(DefaultDashboardId, "Main Dashboard", DashboardType.List, true, 1),
            new DashboardResponse(Guid.Parse("00000000-0000-0000-0000-000000000003"), "Workshop", DashboardType.List, false, 1)
        ]);

    public Task<PrinterResponse> GetPrinter(Guid workspaceId, Guid printerId, CancellationToken cancellationToken) =>
        Task.FromResult(new PrinterResponse(printerId, "Voron 2.4", "octagrid.moonraker", VoronSettings, NoSecrets, "Connected", true, 1));

    public Task<List<PrinterResponse>> GetPrintersList(Guid workspaceId, GetPrintersQuery query, CancellationToken cancellationToken) =>
        Task.FromResult<List<PrinterResponse>>([
            new PrinterResponse(VoronPrinterId, "Voron 2.4", "octagrid.moonraker", VoronSettings, NoSecrets, "Connected", true, 1),
            new PrinterResponse(BambuPrinterId, "Bambu Lab X1C", "octagrid.bambu-mqtt", new Dictionary<string, string> { ["url"] = "192.168.1.51" }, NoSecrets, "Connected", true, 1)
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

    public Task DeleteDashboard(Guid dashboardId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<DashboardResponse> SetDefaultDashboard(Guid dashboardId, CancellationToken cancellationToken) =>
        Task.FromResult(new DashboardResponse(dashboardId, "Main Dashboard", DashboardType.List, true, 1));

    public Task<DashboardResponse> UpdateDashboard(Guid dashboardId, UpdateDashboardRequest request, CancellationToken cancellationToken) =>
         Task.FromResult(new DashboardResponse(dashboardId, request.Name, DashboardType.List, false, 1));

    public Task<PrinterResponse> UpdateIdentity(Guid printerId, UpdatePrinterIdentityRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new PrinterResponse(printerId, "Voron 2.4", "octagrid.moonraker", VoronSettings, NoSecrets, "Connected", true, 1));

    public Task<PrinterResponse> UpdatePrinter(Guid printerId, UpdatePrinterRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new PrinterResponse(printerId, request.Name ?? string.Empty, request.ConnectorId ?? string.Empty, request.Settings ?? NoSecrets, request.Secrets ?? NoSecrets, "Connected", request.Enabled, 1));

    public Task<WorkspaceResponse> UpdateWorkspace(Guid workspaceId, string name, CancellationToken cancellationToken) =>
        Task.FromResult(new WorkspaceResponse(workspaceId, name, 1));
}
