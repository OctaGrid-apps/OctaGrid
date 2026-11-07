using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OctaGrid.Agent.Core;
using OctaGrid.Agent.Core.Connectors;
using OctaGrid.Extensions.Abstractions;
using OctaGrid.Application.Services;
using OctaGrid.Application.Telemetry;
using OctaGrid.Client.Contracts;
using OctaGrid.Data.Abstractions;
using OctaGrid.Protocol.Workspaces;
using OctaGrid.Data.Abstractions.Models;

namespace OctaGrid.Client.Local;

public static class DependencyInjection
{
    public static IServiceCollection AddOctoGridClientLocal(this IServiceCollection services)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = GetDatabasePath(),
            Pooling = true,
            DefaultTimeout = 30
        }.ToString();

        services.AddDbContext<OctaGridContext>(options =>
                options.UseSqlite(connectionString, b => b.MigrationsAssembly("OctaGrid.Client.Local")));

        services
            .AddScoped<IPasswordHasher<User>, PasswordHasher<User>>()
            .AddSingleton<AgentsService>()
            .AddSingleton<UsersService>()
            .AddSingleton<PrintersService>()
            .AddSingleton<DashboardsService>()
            .AddSingleton<WorkspacesService>();

        services.AddSingleton<EntityChangeFeed>();
        services.AddSingleton<LocalAgentSynchronizer>();
        services.AddSingleton<IMessagePublisher, LocalMessageHandler>();
        services.AddSingleton<IMessageHandler, MessageHandler>();
        services.AddSingleton<ITelemetrySink, LocalTelemetryHandler>();

        services.AddSingleton<OctaGridClientLocal>();
        services.AddSingleton<IUsersClient>(sp => sp.GetRequiredService<OctaGridClientLocal>());
        services.AddSingleton<IWorkspacesClient>(sp => sp.GetRequiredService<OctaGridClientLocal>());
        services.AddSingleton<IDashboardsClient>(sp => sp.GetRequiredService<OctaGridClientLocal>());
        services.AddSingleton<IPrintersClient>(sp => sp.GetRequiredService<OctaGridClientLocal>());
        services.AddSingleton<IAgentsClient>(sp => sp.GetRequiredService<OctaGridClientLocal>());

        services.AddSingleton<IExtensionStateStore>(
            _ => new FileExtensionStateStore(Path.Combine(OctaGridDataPaths.DataFolder, "extensions.json")));

        // The manager owns the registry: it starts with the bundled connectors and adds the ones found in
        // <data>/extensions before anything can create a session.
        services.AddSingleton(sp =>
        {
            var loggerFactory = sp.GetService<ILoggerFactory>();
            var manager = new ExtensionManager(
                BundledConnectors.CreateRegistry(loggerFactory),
                sp.GetRequiredService<IExtensionStateStore>(),
                new ExtensionLoader(loggerFactory: loggerFactory),
                OctaGridDataPaths.ExtensionsFolder);
            manager.LoadInstalledExtensions();
            return manager;
        });
        services.AddSingleton(sp => sp.GetRequiredService<ExtensionManager>().Registry);
        services.AddSingleton<IExtensionsClient, LocalExtensionsClient>();
        services.AddSingleton<AgentWorker>();
        services.AddSingleton<IClientRuntime, LocalClientRuntime>();
        services.AddSingleton<IAgentCommandsService, LocalAgentCommands>();
        services.AddSingleton<IPrinterProfileSource, LocalPrinterProfileSource>();

        return services;
    }

    public static void ApplyMigrations(this IServiceProvider provider)
    {
        var logger = provider.GetRequiredService<ILogger<OctaGridContext>>();
        var context = provider.GetRequiredService<OctaGridContext>();

        try
        {
            logger.LogInformation("Applying database migrations for OctaGridContext...");
            context.Database.Migrate();

            context.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA temp_store=MEMORY;");

            logger.LogInformation("Database migrations applied successfully.");

            EnsureLocalDefaults(provider, context, logger);
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
            var user = context.Users.FirstOrDefault();
            if (user is null)
            {
                logger.LogInformation("Provisioning default local user...");
                var hasher = provider.GetRequiredService<IPasswordHasher<User>>();
                user = new User
                {
                    Name = "User",
                    Email = "User@example.com"
                };
                user.PasswordHash = hasher.HashPassword(user, Guid.NewGuid().ToString("N"));
                context.Users.Add(user);
                context.SaveChanges();

                var currentUser = provider.GetService<ICurrentUserAccessor>();
                currentUser?.SetCurrentUserId(user.Id);
                logger.LogInformation("Default local user {UserId} provisioned successfully.", user.Id);
            }
            else
            {
                var currentUser = provider.GetService<ICurrentUserAccessor>();
                if (currentUser?.GetCurrentUserId() == null)
                {
                    currentUser?.SetCurrentUserId(user.Id);
                }
            }

            var workspacesWithoutDashboards = context.Workspaces
                .Include(w => w.Dashboards)
                .Where(w => !w.Dashboards.Any())
                .ToList();

            if (workspacesWithoutDashboards.Count > 0)
            {
                logger.LogInformation("Found {Count} workspace(s) without a dashboard; provisioning default dashboard...", workspacesWithoutDashboards.Count);
                foreach (var ws in workspacesWithoutDashboards)
                {
                    ws.Dashboards.Add(new Dashboard
                    {
                        Name = "Default",
                        Type = DashboardType.List,
                        IsDefault = true
                    });
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

    private static string GetDatabasePath() => OctaGridDataPaths.DatabasePath;
}
