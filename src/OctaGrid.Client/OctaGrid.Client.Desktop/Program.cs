using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Desktop.Services;
using OctaGrid.Client.Local;
using OctaGrid.Client.Services;
using Serilog;

namespace OctaGrid.Client.Desktop;

internal sealed class Program
{
    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        DesktopLogging.Initialize();
        Log.Information("Starting OctaGrid Desktop {Version}...", Helper.GetInfoVersion());
        Log.Information("Logs directory: {LogsDirectory}", Helper.LogsDirectory);

        try
        {
            var services = new ServiceCollection();
            services.AddDesktopLogging();
            services.AddSingleton<ICurrentUserAccessor, CurrentUserAccessor>();
            services.AddSingleton<IAppSettingsStore, JsonClientSettingsStore>();
            services
                .AddWebRtc();

            //if (settingsStore.ClientSettings.Mode == ClientMode.Local)
            {
                services.AddLocalMode();
            }
            //else if (settingsStore.ClientSettings.Mode == ClientMode.Remote)
            //{
            //    services.AddRemoteMode();
            //}

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "OctaGrid Desktop terminated unexpectedly");
            throw;
        }
        finally
        {
            Log.Information("OctaGrid Desktop shutting down.");
            Log.CloseAndFlush();
        }
    }
}

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

        //    options.SignalingServerUrl = $"{signalingServerHost}/hubs/signaling";
        //    options.IsOfferer = true;
        //});

        //services.AddTransient<IWebRtcPeer, WasmWebRtcPeer>();
    }
}
