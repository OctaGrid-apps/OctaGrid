using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OctaGrid.Agent.Core;
using OctaGrid.Application.Services;
using OctaGrid.Application.Telemetry;
using OctaGrid.Client.Contracts;
using OctaGrid.Data.Abstractions;
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
        services.AddSingleton<ITelemetryService, LocalTelemetryHandler>();

        services.AddSingleton<OctaGridClientLocal>();
        services.AddSingleton<IUsersClient>(sp => sp.GetRequiredService<OctaGridClientLocal>());
        services.AddSingleton<IWorkspacesClient>(sp => sp.GetRequiredService<OctaGridClientLocal>());
        services.AddSingleton<IDashboardsClient>(sp => sp.GetRequiredService<OctaGridClientLocal>());
        services.AddSingleton<IPrintersClient>(sp => sp.GetRequiredService<OctaGridClientLocal>());
        services.AddSingleton<IAgentsClient>(sp => sp.GetRequiredService<OctaGridClientLocal>());

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
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while applying database migrations.");
            throw;
        }
    }

    private static string GetDatabasePath() => OctaGridDataPaths.DatabasePath;
}
