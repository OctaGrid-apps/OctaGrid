using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OctaGrid.Agent.Core;
using OctaGrid.Agent.Core.Extensions;
using OctaGrid.Agent.Core.Link;
using OctaGrid.Extensions.Abstractions;

namespace OctaGrid.Agent.Hosting;

/// <summary>Registers an agent in a host's container.</summary>
public static class AgentHosting
{
    /// <summary>
    /// Registers the agent: the built-in connectors, the extension manager with its state, the
    /// <see cref="AgentWorker"/> and the agent's side of the link to a backend
    /// (<see cref="AgentRequestDispatcher"/> for requests, <see cref="AgentNotificationHub"/> for what the agent
    /// reports). The host connects those two to its transport; the agent itself needs nothing else.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">
    /// Returns the agent's file locations. It is called when the agent is first resolved, not during
    /// registration, so registering touches no files.
    /// </param>
    public static IServiceCollection AddOctaGridAgent(this IServiceCollection services, Func<AgentHostingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton<IExtensionStateStore>(_ => new FileExtensionStateStore(options().ExtensionStatePath));

        // The manager owns the registry: it starts with the built-in connectors and adds the installed
        // extensions before anything can create a session.
        services.AddSingleton(sp =>
        {
            var loggerFactory = sp.GetService<ILoggerFactory>();
            var manager = new ExtensionManager(
                BuiltInConnectors.CreateRegistry(loggerFactory),
                sp.GetRequiredService<IExtensionStateStore>(),
                new ExtensionLoader(loggerFactory: loggerFactory),
                options().ExtensionsFolder);
            manager.LoadInstalledExtensions();
            return manager;
        });
        services.AddSingleton(sp => sp.GetRequiredService<ExtensionManager>().Registry);
        services.AddSingleton<IMessageHandler, MessageHandler>();

        // Telemetry from the printer sessions goes to the hub, where the host's transport picks it up.
        services.AddSingleton<AgentNotificationHub>();
        services.AddSingleton<ITelemetrySink>(sp => sp.GetRequiredService<AgentNotificationHub>());
        services.AddSingleton(sp => new AgentUploads(
            options().UploadsFolder ?? Path.Combine(Path.GetTempPath(), "OctaGrid", "uploads"),
            logger: sp.GetService<ILogger<AgentUploads>>()));
        services.AddSingleton<AgentWorker>();
        services.AddSingleton<AgentRequestDispatcher>();

        return services;
    }
}
