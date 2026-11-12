using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OctaGrid.Agent.Core;
using OctaGrid.Agent.Core.Extensions;

namespace OctaGrid.Agent.Hosting;

/// <summary>Registers an agent in a host's container.</summary>
public static class AgentHosting
{
    /// <summary>
    /// Registers the agent: the built-in connectors, the extension manager with its state, and the
    /// <see cref="AgentWorker"/>. The host supplies what differs per deployment by registering an
    /// <c>ITelemetrySink</c> (where telemetry goes) and an <see cref="IPrinterProfileSource"/> (which printers
    /// to run).
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
        services.AddSingleton<AgentWorker>();

        return services;
    }
}
