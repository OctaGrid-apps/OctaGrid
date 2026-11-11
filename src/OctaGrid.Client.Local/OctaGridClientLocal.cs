using Microsoft.Extensions.DependencyInjection;
using OctaGrid.Application.Services;
using OctaGrid.Client.Contracts;
using OctaGrid.Protocol;
using OctaGrid.Protocol.Agents;
using OctaGrid.Protocol.Dashboards;
using OctaGrid.Protocol.Printers;
using OctaGrid.Protocol.Workspaces;

namespace OctaGrid.Client.Local;

// Deliberately does NOT implement IAuthClient — local mode has no login (single implicit-owner account, auto-provisioned via Me()).
//
// A singleton, while the application services and their DbContext are scoped. Every call opens its own scope,
// the way a server handles one request, so calls from the UI and the agent never share a DbContext.
internal sealed class OctaGridClientLocal : IUsersClient, IWorkspacesClient, IPrintersClient, IAgentsClient, IDashboardsClient
{
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IServiceScopeFactory _scopes;

    public OctaGridClientLocal(IServiceScopeFactory scopes, ICurrentUserAccessor currentUser)
    {
        _scopes = scopes;
        _currentUser = currentUser;
    }

    public Task AddPrinter(Guid agentId, Guid printerId, CancellationToken cancellationToken) =>
        Run<AgentsService>((agents, userId) => agents.AddPrinter(userId, agentId, printerId, cancellationToken), cancellationToken);

    public Task<AgentResponse> CreateAgent(Guid workspaceId, string name, CancellationToken cancellationToken) =>
        Run<AgentsService, AgentResponse>((agents, userId) => agents.Create(userId, workspaceId, new CreateAgentRequest(name), cancellationToken), cancellationToken);

    public Task<DashboardResponse> CreateDashboard(Guid workspaceId, CreateDashboardRequest request, CancellationToken cancellationToken) =>
        Run<DashboardsService, DashboardResponse>((dashboards, userId) => dashboards.Create(userId, workspaceId, request, cancellationToken), cancellationToken);

    public Task<PrinterResponse> CreatePrinter(Guid workspaceId, CreatePrinterRequest request, CancellationToken cancellationToken) =>
        Run<PrintersService, PrinterResponse>((printers, userId) => printers.Create(userId, workspaceId, request, cancellationToken), cancellationToken);

    public Task<WorkspaceResponse> CreateWorkspace(string name, CancellationToken cancellationToken) =>
        Run<WorkspacesService, WorkspaceResponse>((workspaces, userId) => workspaces.Create(userId, new CreateWorkspaceRequest(name), cancellationToken), cancellationToken);

    public Task DeleteDashboard(Guid dashboardId, CancellationToken cancellationToken) =>
        Run<DashboardsService>((dashboards, userId) => dashboards.Delete(userId, dashboardId, cancellationToken), cancellationToken);

    public Task DeletePrinter(Guid printerId, CancellationToken cancellationToken) =>
        Run<PrintersService>((printers, userId) => printers.Delete(userId, printerId, cancellationToken), cancellationToken);

    public Task<List<AgentResponse>> GetAgentList(Guid workspaceId, CancellationToken cancellationToken) =>
        Run<AgentsService, List<AgentResponse>>((agents, userId) => agents.GetList(userId, workspaceId, cancellationToken), cancellationToken);

    public Task<List<DashboardResponse>> GetDashboardList(Guid workspaceId, CancellationToken cancellationToken) =>
        Run<DashboardsService, List<DashboardResponse>>((dashboards, userId) => dashboards.GetList(userId, workspaceId, cancellationToken), cancellationToken);

    public Task<PrinterResponse> GetPrinter(Guid workspaceId, Guid printerId, CancellationToken cancellationToken) =>
        Run<PrintersService, PrinterResponse>((printers, userId) => printers.GetPrinter(userId, workspaceId, printerId, cancellationToken), cancellationToken);

    public Task<List<PrinterResponse>> GetPrintersList(Guid workspaceId, GetPrintersQuery query, CancellationToken cancellationToken) =>
        Run<PrintersService, List<PrinterResponse>>((printers, userId) => printers.GetPrintersList(userId, workspaceId, query, cancellationToken), cancellationToken);

    public Task<List<WorkspaceResponse>> GetWorkspaceList(CancellationToken cancellationToken) =>
        Run<WorkspacesService, List<WorkspaceResponse>>((workspaces, userId) => workspaces.GetList(userId, cancellationToken), cancellationToken);

    public Task<List<WorkspaceSummaryResponse>> GetWorkspacesSummary(CancellationToken cancellationToken) =>
        Run<WorkspacesService, List<WorkspaceSummaryResponse>>((workspaces, userId) => workspaces.GetSummary(userId, cancellationToken), cancellationToken);

    public async Task<UserResponse> Me(CancellationToken cancellationToken)
    {
        await using var scope = _scopes.CreateAsyncScope();
        return await GetOrCreateUserAsync(scope.ServiceProvider, cancellationToken);
    }

    public Task<DashboardResponse> SetDefaultDashboard(Guid dashboardId, CancellationToken cancellationToken) =>
        Run<DashboardsService, DashboardResponse>((dashboards, userId) => dashboards.SetDefault(userId, dashboardId, cancellationToken), cancellationToken);

    public Task SetPrinterEnabled(Guid printerId, bool state, CancellationToken cancellationToken) =>
        Run<PrintersService>((printers, userId) => printers.SetEnabled(userId, printerId, state, cancellationToken), cancellationToken);

    public Task<AgentResponse> UpdateAgent(Guid agentId, string name, CancellationToken cancellationToken) =>
        Run<AgentsService, AgentResponse>((agents, userId) => agents.Update(userId, agentId, new UpdateAgentRequest(name), cancellationToken), cancellationToken);

    public Task<DashboardResponse> UpdateDashboard(Guid dashboardId, UpdateDashboardRequest request, CancellationToken cancellationToken) =>
        Run<DashboardsService, DashboardResponse>((dashboards, userId) => dashboards.Update(userId, dashboardId, request, cancellationToken), cancellationToken);

    public Task<PrinterResponse> UpdateIdentity(Guid printerId, UpdatePrinterIdentityRequest request, CancellationToken cancellationToken) =>
        Run<PrintersService, PrinterResponse>((printers, userId) => printers.UpdateIdentity(userId, printerId, request, cancellationToken), cancellationToken);

    public Task<PrinterResponse> UpdatePrinter(Guid printerId, UpdatePrinterRequest request, CancellationToken cancellationToken) =>
        Run<PrintersService, PrinterResponse>((printers, userId) => printers.Update(userId, printerId, request, cancellationToken), cancellationToken);

    public Task<WorkspaceResponse> UpdateWorkspace(Guid workspaceId, string name, CancellationToken cancellationToken) =>
        Run<WorkspacesService, WorkspaceResponse>((workspaces, userId) => workspaces.Update(userId, workspaceId, new UpdateWorkspaceRequest(name), cancellationToken), cancellationToken);

    private async Task<UserResponse> GetOrCreateUserAsync(IServiceProvider scoped, CancellationToken cancellationToken)
    {
        var user = await scoped.GetRequiredService<UsersService>().GetOrCreate(cancellationToken);
        _currentUser.SetCurrentUserId(user.Id);
        return user;
    }

    private async ValueTask<Guid> GetUserIdAsync(IServiceProvider scoped, CancellationToken cancellationToken)
    {
        if (_currentUser.GetCurrentUserId() is { } id)
        {
            return id;
        }

        return (await GetOrCreateUserAsync(scoped, cancellationToken)).Id;
    }

    /// <summary>Runs one call against a service in its own scope, as the current user.</summary>
    private async Task<TResult> Run<TService, TResult>(Func<TService, Guid, Task<TResult>> call, CancellationToken cancellationToken)
        where TService : notnull
    {
        await using var scope = _scopes.CreateAsyncScope();
        var userId = await GetUserIdAsync(scope.ServiceProvider, cancellationToken);
        return await call(scope.ServiceProvider.GetRequiredService<TService>(), userId);
    }

    private async Task Run<TService>(Func<TService, Guid, Task> call, CancellationToken cancellationToken)
        where TService : notnull
    {
        await using var scope = _scopes.CreateAsyncScope();
        var userId = await GetUserIdAsync(scope.ServiceProvider, cancellationToken);
        await call(scope.ServiceProvider.GetRequiredService<TService>(), userId);
    }
}
