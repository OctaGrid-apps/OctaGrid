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

    private Guid CurrentUserId => _currentUser.GetCurrentUserId()
        ?? throw new InvalidOperationException("No current user — call Me() first to auto-provision the local owner.");

    public Task AddPrinter(Guid agentId, Guid printerId, CancellationToken cancellationToken)
    {
        return _agents.AddPrinter(CurrentUserId, agentId, printerId, cancellationToken);
    }

    public Task<AgentResponse> CreateAgent(Guid workspaceId, string name, CancellationToken cancellationToken)
    {
        return _agents.Create(CurrentUserId, workspaceId, new CreateAgentRequest(name), cancellationToken);
    }

    public Task<DashboardResponse> CreateDashboard(Guid workspaceId, CreateDashboardRequest request, CancellationToken cancellationToken)
    {
        return _dashboards.Create(CurrentUserId, workspaceId, request, cancellationToken);
    }

    public Task<PrinterResponse> CreatePrinter(Guid workspaceId, CreatePrinterRequest request, CancellationToken cancellationToken)
    {
        return _printers.Create(CurrentUserId, workspaceId, request, cancellationToken);
    }

    public Task<WorkspaceResponse> CreateWorkspace(string name, CancellationToken cancellationToken)
    {
        return _workspaces.Create(CurrentUserId, new CreateWorkspaceRequest(name), cancellationToken);
    }

    public Task DeletePrinter(Guid printerId, CancellationToken cancellationToken)
    {
        return _printers.Delete(CurrentUserId, printerId, cancellationToken);
    }

    public Task<List<AgentResponse>> GetAgentList(Guid workspaceId, CancellationToken cancellationToken)
    {
        return _agents.GetList(CurrentUserId, workspaceId, cancellationToken);
    }

    public Task<List<DashboardResponse>> GetDashboardList(Guid workspaceId, CancellationToken cancellationToken)
    {
        return _dashboards.GetList(CurrentUserId, workspaceId, cancellationToken);
    }

    public Task<PrinterResponse> GetPrinter(Guid workspaceId, Guid printerId, CancellationToken cancellationToken)
    {
        return _printers.GetPrinter(CurrentUserId, workspaceId, printerId, cancellationToken);
    }

    public Task<List<PrinterResponse>> GetPrintersList(Guid workspaceId, GetPrintersQuery query, CancellationToken cancellationToken)
    {
        return _printers.GetPrintersList(CurrentUserId, workspaceId, query, cancellationToken);
    }

    public Task<List<WorkspaceResponse>> GetWorkspaceList(CancellationToken cancellationToken)
    {
        return _workspaces.GetList(CurrentUserId, cancellationToken);
    }

    public Task<List<WorkspaceSummaryResponse>> GetWorkspacesSummary(CancellationToken cancellationToken)
    {
        return _workspaces.GetSummary(CurrentUserId, cancellationToken);
    }

    public async Task<UserResponse> Me(CancellationToken cancellationToken)
    {
        var userResponse = await _users.GetOrCreate(cancellationToken);
        _currentUser.SetCurrentUserId(userResponse.Id);
        return userResponse;
    }

    public Task SetPrinterEnabled(Guid printerId, bool state, CancellationToken cancellationToken)
    {
        return _printers.SetEnabled(CurrentUserId, printerId, state, cancellationToken);
    }

    public Task<AgentResponse> UpdateAgent(Guid agentId, string name, CancellationToken cancellationToken)
    {
        return _agents.Update(CurrentUserId, agentId, new UpdateAgentRequest(name), cancellationToken);
    }

    public Task<PrinterResponse> UpdateIdentity(Guid printerId, UpdatePrinterIdentityRequest request, CancellationToken cancellationToken)
    {
        return _printers.UpdateIdentity(CurrentUserId, printerId, request, cancellationToken);
    }

    public Task<PrinterResponse> UpdatePrinter(Guid printerId, UpdatePrinterRequest request, CancellationToken cancellationToken)
    {
        return _printers.Update(CurrentUserId, printerId, request, cancellationToken);
    }

    public Task<WorkspaceResponse> UpdateWorkspace(Guid workspaceId, string name, CancellationToken cancellationToken)
    {
        return _workspaces.Update(CurrentUserId, workspaceId, new UpdateWorkspaceRequest(name), cancellationToken);
    }
}
