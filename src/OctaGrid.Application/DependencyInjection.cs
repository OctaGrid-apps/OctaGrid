using Microsoft.Extensions.DependencyInjection;
using OctaGrid.Application.Agents;
using OctaGrid.Application.Authorization;
using OctaGrid.Application.Events;
using OctaGrid.Application.Identity;
using OctaGrid.Application.Secrets;
using OctaGrid.Application.Services;
using OctaGrid.Application.Telemetry;
using OctaGrid.Data;

namespace OctaGrid.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the application layer: services, authorization, secrets and domain event delivery.
    /// Every host that runs the backend calls this.
    /// </summary>
    /// <remarks>
    /// The host also registers an <c>OctaGridContext</c> provider, an <see cref="IAgentLink"/>, an
    /// <see cref="IMessagePublisher"/> and an <see cref="ISecretKeySource"/>.
    /// </remarks>
    public static IServiceCollection AddOctaGridApplication(this IServiceCollection services)
    {
        // Scoped, like the DbContext they use. Hosts open a scope per call or per request.
        services
            .AddScoped<WorkspaceAccess>()
            .AddScoped<PrinterRouting>()
            .AddScoped<PrinterSecretStore>()
            .AddScoped<PrinterProfiles>()
            .AddScoped<AgentsService>()
            .AddScoped<UserProvisioning>()
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

        // Domain events are saved to the outbox with their change and delivered from there.
        services.AddSingleton<OutboxDispatcher>();
        services.AddSingleton<IOutboxSignal>(sp => sp.GetRequiredService<OutboxDispatcher>());
        services.AddScoped<IDomainEventHandler, EntityChangePublisher>();
        services.AddScoped<IDomainEventHandler, AgentProfilesPublisher>();
        services.AddSingleton<PrinterStateRecorder>();

        // The key comes from the host (ISecretKeySource).
        services.AddSingleton<ISecretProtector, AesGcmSecretProtector>();

        return services;
    }
}
