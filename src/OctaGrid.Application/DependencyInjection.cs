using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using OctaGrid.Application.Agents;
using OctaGrid.Application.Authorization;
using OctaGrid.Application.Events;
using OctaGrid.Application.Secrets;
using OctaGrid.Application.Services;
using OctaGrid.Application.Telemetry;
using OctaGrid.Data;
using OctaGrid.Data.Models;

namespace OctaGrid.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the application layer: services, authorization, secrets and domain event delivery. Every host that
    /// runs the backend (Local mode today, the server later) calls this, so they compose it the same way.
    /// </summary>
    /// <remarks>
    /// The host also registers an <c>OctaGridContext</c> provider, an <see cref="IAgentLink"/>, an
    /// <see cref="IMessagePublisher"/> and an <see cref="ISecretKeySource"/>.
    /// </remarks>
    public static IServiceCollection AddOctaGridApplication(this IServiceCollection services)
    {
        // Scoped, like the context they use: a DbContext is not thread-safe and must not live for the whole
        // process. Hosts open one scope per call (Local mode) or per request (a server).
        services
            .AddScoped<IPasswordHasher<User>, PasswordHasher<User>>()
            .AddScoped<WorkspaceAccess>()
            .AddScoped<PrinterRouting>()
            .AddScoped<PrinterSecretStore>()
            .AddScoped<PrinterProfiles>()
            .AddScoped<AgentsService>()
            .AddScoped<UsersService>()
            .AddScoped<PrintersService>()
            .AddScoped<PrinterCommandsService>()
            .AddScoped<PrinterWatchService>()
            .AddScoped<ExtensionsService>()
            .AddScoped<AgentProfilesService>()
            .AddScoped<DashboardsService>()
            .AddScoped<PrinterStateService>()
            .AddScoped<WorkspacesService>();

        services.AddSingleton<IPermissionPolicy, RolePermissionPolicy>();
        services.AddSingleton<PrinterWatchRegistry>();

        // Domain events are saved to the outbox with the change that raised them and delivered from there.
        services.AddSingleton<OutboxDispatcher>();
        services.AddSingleton<IOutboxSignal>(sp => sp.GetRequiredService<OutboxDispatcher>());
        services.AddScoped<IDomainEventHandler, EntityChangePublisher>();
        services.AddScoped<IDomainEventHandler, AgentProfilesPublisher>();
        services.AddSingleton<PrinterStateRecorder>();

        // The key comes from the host (ISecretKeySource); the encrypted secrets live in the database.
        services.AddSingleton<ISecretProtector, AesGcmSecretProtector>();

        return services;
    }
}
