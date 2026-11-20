using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OctaGrid.Client.Desktop.Services;
using OctaGrid.Client.Local;

namespace OctaGrid.Client.Desktop;

public static class Extensions
{
    private const string SettingsFileName = "appsettings.json";

    /// <summary>
    /// Registers the configuration from <c>appsettings.json</c> next to the executable and the options bound from it.
    /// </summary>
    public static IServiceCollection AddDesktopConfiguration(this IServiceCollection services)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile(SettingsFileName, optional: true, reloadOnChange: false)
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
        return services.AddStoreOptions(configuration);
    }

    /// <summary>Binds <see cref="StoreOptions"/> to the <c>Store</c> section of <paramref name="configuration"/>.</summary>
    public static IServiceCollection AddStoreOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<StoreOptions>().Bind(configuration.GetSection(StoreOptions.SectionName));
        return services;
    }

    // Local/Remote composition lives here, in the Desktop head, not in the shared UI project —
    // only Client.Desktop is allowed to reference Client.Local (SQLite, the printer agent).
    public static IServiceCollection AddLocalMode(this IServiceCollection services)
    {
        services.AddOctoGridClientLocal();
        App.RegisterAppServices(services);
        (App.Services ?? throw new InvalidOperationException("Application services were not registered.")).ApplyMigrations();
        return services;
    }

    public static IServiceCollection AddRemoteMode(this IServiceCollection services)
    {
        App.RegisterAppServices(services);
        return services;
    }

    public static void AddWebRtc(this IServiceCollection services)
    {
        //services.AddOptions<WebRtcOptions>().Configure<IConfiguration>((options, configuration) =>
        //{
        //    var signalingServerHost = configuration["services:server:http:0"] ?? configuration["services:server:https:0"];
        //
        //    options.SignalingServerUrl = $"{signalingServerHost}/hubs/signaling";
        //    options.IsOfferer = true;
        //});

        //services.AddTransient<IWebRtcPeer, WasmWebRtcPeer>();
    }
}
