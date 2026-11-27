using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OctaGrid.Agent.Hosting;
using OctaGrid.Application.Agents;
using OctaGrid.Application;
using OctaGrid.Application.Telemetry;
using OctaGrid.Client.Contracts;
using OctaGrid.Data;
using OctaGrid.Data.Models;
using OctaGrid.Data.Sqlite;
using OctaGrid.Protocol.Dashboards;

namespace OctaGrid.Client.Local;

public static class DependencyInjection
{
    public static IServiceCollection AddOctoGridClientLocal(this IServiceCollection services)
    {
        // The path is read when the first context is created, not here, so registering Local mode touches no files.
        services.AddOctaGridSqlite(GetDatabasePath);

        // The Local adapters below are singletons and open one scope per call, as a server does per request.
        services.AddOctaGridApplication();

        services.AddSingleton<IMessagePublisher, LocalMessageHandler>();

        services.AddSingleton<OctaGridClientLocal>();
        services.AddSingleton<IUsersClient>(sp => sp.GetRequiredService<OctaGridClientLocal>());
        services.AddSingleton<IWorkspacesClient>(sp => sp.GetRequiredService<OctaGridClientLocal>());
        services.AddSingleton<IDashboardsClient>(sp => sp.GetRequiredService<OctaGridClientLocal>());
        services.AddSingleton<IPrintersClient>(sp => sp.GetRequiredService<OctaGridClientLocal>());
        services.AddSingleton<IAgentsClient>(sp => sp.GetRequiredService<OctaGridClientLocal>());

        // The in-process agent, and the link the rest of Local mode reaches it through. Nothing below
        // talks to the agent directly: requests and what the agent reports both go over IAgentLink.
        services.AddOctaGridAgent(() => new AgentHostingOptions(
            OctaGridDataPaths.ExtensionsFolder,
            Path.Combine(OctaGridDataPaths.DataFolder, "extensions.json"),
            // Its own folder, so an Agent app on the same machine never prunes the in-process agent's printers.
            FileCacheFolder: Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OctaGrid.Data", "file-cache", "local")));

        services.AddSingleton<LocalAgent>();
        services.AddSingleton<IAgentLink, InProcessAgentLink>();
        services.AddSingleton<LocalAgentNotifications>();
        services.AddSingleton<LocalCalls>();

        services.AddSingleton<IExtensionsClient, LocalExtensionsClient>();
        services.AddSingleton<IClientRuntime, LocalClientRuntime>();
        services.AddSingleton<IAgentCommandsService, LocalAgentCommands>();
        services.AddSingleton<IPrinterTelemetryClient, LocalPrinterTelemetryClient>();

        return services;
    }

    public static void ApplyMigrations(this IServiceProvider provider)
    {
        var logger = provider.GetRequiredService<ILogger<OctaGridContext>>();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OctaGridContext>();

        try
        {
            logger.LogInformation("Applying database migrations for OctaGridContext...");
            context.MigrateSqlite();

            logger.LogInformation("Database migrations applied successfully.");

            EnsureLocalDefaults(scope.ServiceProvider, context, logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while applying database migrations.");
            throw;
        }
    }

    private static void EnsureLocalDefaults(IServiceProvider provider, OctaGridContext context, ILogger logger)
    {
        try
        {
            var user = context.Users.FirstOrDefault(User.WithLogin(LocalUser.Issuer, LocalUser.Subject));
            if (user is null)
            {
                user = User.Create(LocalUser.Identity.Name, LocalUser.Identity.Email);
                user.AddLogin(LocalUser.Issuer, LocalUser.Subject);
                context.Users.Add(user);
                context.SaveChanges();
                logger.LogInformation("Provisioned the local user {UserId}", user.Id);
            }

            provider.GetService<ICurrentUserAccessor>()?.SetCurrentUserId(user.Id);

            EnsureLocalAgent(provider, context, user.Id, logger);

            var workspacesWithoutDashboards = context.Workspaces
                .Include(w => w.Dashboards)
                .Where(w => !w.Dashboards.Any())
                .ToList();

            if (workspacesWithoutDashboards.Count > 0)
            {
                logger.LogInformation("Found {Count} workspace(s) without a dashboard; provisioning default dashboard...", workspacesWithoutDashboards.Count);
                foreach (var ws in workspacesWithoutDashboards)
                {
                    ws.EnsureDashboard();
                }
                context.SaveChanges();
                logger.LogInformation("Default dashboards provisioned for existing workspaces without dashboards.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to ensure default local data.");
        }
    }

    /// <summary>
    /// Makes sure the local user has a workspace and that the in-process agent has a row in it, so printers can be
    /// assigned to it like to any other agent. Local mode has one agent: the first one of the user's workspaces.
    /// </summary>
    private static void EnsureLocalAgent(IServiceProvider provider, OctaGridContext context, Guid userId, ILogger logger)
    {
        var workspaceIds = context.WorkspaceMembers
            .Where(m => m.UserId == userId)
            // Ids are version 7 GUIDs, so they sort by creation time; SQLite cannot sort DateTimeOffset columns.
            .OrderBy(m => m.WorkspaceId)
            .Select(m => m.WorkspaceId)
            .ToList();

        if (workspaceIds.Count == 0)
        {
            var workspace = Workspace.Create("Default", userId, DateTimeOffset.UtcNow);
            context.Workspaces.Add(workspace);
            context.SaveChanges();

            logger.LogInformation("Provisioned default workspace {WorkspaceId} for local user {UserId}", workspace.Id, userId);
            workspaceIds.Add(workspace.Id);
        }

        var agentId = context.Agents
            .Where(a => workspaceIds.Contains(a.WorkspaceId))
            // Ids are version 7 GUIDs, so they sort by creation time; SQLite cannot sort DateTimeOffset columns.
            .OrderBy(a => a.Id)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefault();

        if (agentId is null)
        {
            // Qualified: inside OctaGrid.* a bare Agent is the OctaGrid.Agent namespace.
            var agent = new OctaGrid.Data.Models.Agent
            {
                Name = "Local",
                WorkspaceId = workspaceIds[0],
                Status = new AgentStatus { State = AgentState.Active, ActivatedAt = DateTimeOffset.UtcNow },
            };
            context.Agents.Add(agent);
            context.SaveChanges();

            logger.LogInformation("Provisioned local agent {AgentId} in workspace {WorkspaceId}", agent.Id, agent.WorkspaceId);
            agentId = agent.Id;
        }

        provider.GetRequiredService<LocalAgent>().Set(agentId.Value);
    }

    private static string GetDatabasePath() => OctaGridDataPaths.DatabasePath;
}
