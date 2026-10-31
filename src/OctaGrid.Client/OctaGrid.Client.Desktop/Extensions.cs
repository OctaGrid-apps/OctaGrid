using Microsoft.Extensions.DependencyInjection;
using OctaGrid.Client.Local;

namespace OctaGrid.Client.Desktop;

public static class Extensions
{
    // Local/Remote composition lives here, in the Desktop head, not in the shared UI project —
    // only Client.Desktop is allowed to reference Client.Local (SQLite, the printer agent).
    public static IServiceCollection AddLocalMode(this IServiceCollection services)
    {
        services.AddOctoGridClientLocal();
        App.RegisterAppServices(services);
        App.Services.ApplyMigrations();
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
