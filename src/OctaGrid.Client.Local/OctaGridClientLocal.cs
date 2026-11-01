using OctaGrid.Application.Services;
using OctaGrid.Client.Contracts;
using OctaGrid.Protocol;
using OctaGrid.Protocol.Agents;
using OctaGrid.Protocol.Printers;
using OctaGrid.Protocol.Workspaces;

namespace OctaGrid.Client.Local;

// Deliberately does NOT implement IAuthClient — local mode has no login (single implicit-owner account, auto-provisioned via Me()).
internal sealed class OctaGridClientLocal : IUsersClient, IWorkspacesClient, IPrintersClient, IAgentsClient, IDashboardsClient
{
    private readonly AgentsService _agents;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly DashboardsService _dashboards;
    private readonly PrintersService _printers;
    private readonly UsersService _users;
    private readonly WorkspacesService _workspaces;

    public OctaGridClientLocal(UsersService users, AgentsService agents, WorkspacesService workspaces, PrintersService printers, DashboardsService dashboards, ICurrentUserAccessor currentUser)
    {
        _users = users;
        _agents = agents;
        _workspaces = workspaces;
        _printers = printers;
        _dashboards = dashboards;
        _currentUser = currentUser;
    }

    private async ValueTask<Guid> EnsureCurrentUserIdAsync(CancellationToken cancellationToken)
    {
        var id = _currentUser.GetCurrentUserId();
        if (id.HasValue)
        {
            return id.Value;
        }

        var me = await Me(cancellationToken);
        return me.Id;
    }

    public async Task AddPrinter(Guid agentId, Guid printerId, CancellationToken cancellationToken)
    {
        var userId = await EnsureCurrentUserIdAsync(cancellationToken);
        await _agents.AddPrinter(userId, agentId, printerId, cancellationToken);
    }

    public async Task<AgentResponse> CreateAgent(Guid workspaceId, string name, CancellationToken cancellationToken)
    {
        var userId = await EnsureCurrentUserIdAsync(cancellationToken);
        return await _agents.Create(userId, workspaceId, new CreateAgentRequest(name), cancellationToken);
    }

    public async Task<DashboardResponse> CreateDashboard(Guid workspaceId, CreateDashboardRequest request, CancellationToken cancellationToken)
    {
        var userId = await EnsureCurrentUserIdAsync(cancellationToken);
        return await _dashboards.Create(userId, workspaceId, request, cancellationToken);
    }

    public async Task<PrinterResponse> CreatePrinter(Guid workspaceId, CreatePrinterRequest request, CancellationToken cancellationToken)
    {
        var userId = await EnsureCurrentUserIdAsync(cancellationToken);
        return await _printers.Create(userId, workspaceId, request, cancellationToken);
    }

    public async Task<WorkspaceResponse> CreateWorkspace(string name, CancellationToken cancellationToken)
    {
        var userId = await EnsureCurrentUserIdAsync(cancellationToken);
        return await _workspaces.Create(userId, new CreateWorkspaceRequest(name), cancellationToken);
    }

    public async Task DeletePrinter(Guid printerId, CancellationToken cancellationToken)
    {
        var userId = await EnsureCurrentUserIdAsync(cancellationToken);
        await _printers.Delete(userId, printerId, cancellationToken);
    }

    public async Task<List<AgentResponse>> GetAgentList(Guid workspaceId, CancellationToken cancellationToken)
    {
        var userId = await EnsureCurrentUserIdAsync(cancellationToken);
        return await _agents.GetList(userId, workspaceId, cancellationToken);
    }

    public async Task<List<DashboardResponse>> GetDashboardList(Guid workspaceId, CancellationToken cancellationToken)
    {
        var userId = await EnsureCurrentUserIdAsync(cancellationToken);
        return await _dashboards.GetList(userId, workspaceId, cancellationToken);
    }

    public async Task<PrinterResponse> GetPrinter(Guid workspaceId, Guid printerId, CancellationToken cancellationToken)
    {
        var userId = await EnsureCurrentUserIdAsync(cancellationToken);
        return await _printers.GetPrinter(userId, workspaceId, printerId, cancellationToken);
    }

    public async Task<List<PrinterResponse>> GetPrintersList(Guid workspaceId, GetPrintersQuery query, CancellationToken cancellationToken)
    {
        var userId = await EnsureCurrentUserIdAsync(cancellationToken);
        return await _printers.GetPrintersList(userId, workspaceId, query, cancellationToken);
    }

    public async Task<List<WorkspaceResponse>> GetWorkspaceList(CancellationToken cancellationToken)
    {
        var userId = await EnsureCurrentUserIdAsync(cancellationToken);
        return await _workspaces.GetList(userId, cancellationToken);
    }

    public async Task<List<WorkspaceSummaryResponse>> GetWorkspacesSummary(CancellationToken cancellationToken)
    {
        var userId = await EnsureCurrentUserIdAsync(cancellationToken);
        return await _workspaces.GetSummary(userId, cancellationToken);
    }

    public async Task<UserResponse> Me(CancellationToken cancellationToken)
    {
        var userResponse = await _users.GetOrCreate(cancellationToken);
        _currentUser.SetCurrentUserId(userResponse.Id);
        return userResponse;
    }

    public async Task SetPrinterEnabled(Guid printerId, bool state, CancellationToken cancellationToken)
    {
        var userId = await EnsureCurrentUserIdAsync(cancellationToken);
        await _printers.SetEnabled(userId, printerId, state, cancellationToken);
    }

    public async Task<AgentResponse> UpdateAgent(Guid agentId, string name, CancellationToken cancellationToken)
    {
        var userId = await EnsureCurrentUserIdAsync(cancellationToken);
        return await _agents.Update(userId, agentId, new UpdateAgentRequest(name), cancellationToken);
    }

    public async Task<DashboardResponse> UpdateDashboard(Guid dashboardId, UpdateDashboardRequest request, CancellationToken cancellationToken)
    {
        var userId = await EnsureCurrentUserIdAsync(cancellationToken);
        return await _dashboards.Update(userId, dashboardId, request, cancellationToken);
    }

    public async Task<PrinterResponse> UpdateIdentity(Guid printerId, UpdatePrinterIdentityRequest request, CancellationToken cancellationToken)
    {
        var userId = await EnsureCurrentUserIdAsync(cancellationToken);
        return await _printers.UpdateIdentity(userId, printerId, request, cancellationToken);
    }

    public async Task<PrinterResponse> UpdatePrinter(Guid printerId, UpdatePrinterRequest request, CancellationToken cancellationToken)
    {
        var userId = await EnsureCurrentUserIdAsync(cancellationToken);
        return await _printers.Update(userId, printerId, request, cancellationToken);
    }

    public async Task<WorkspaceResponse> UpdateWorkspace(Guid workspaceId, string name, CancellationToken cancellationToken)
    {
        var userId = await EnsureCurrentUserIdAsync(cancellationToken);
        return await _workspaces.Update(userId, workspaceId, new UpdateWorkspaceRequest(name), cancellationToken);
    }
}
