using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using OctaGrid.Application.Secrets;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Desktop.Services;
using OctaGrid.Client.Services;
using Serilog;
using Velopack;

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
        VelopackApp.Build().Run();

        DesktopLogging.Initialize();
        Log.Information("Starting OctaGrid Desktop {Version}...", Helper.GetInfoVersion());
        Log.Information("Logs directory: {LogsDirectory}", Helper.LogsDirectory);

        try
        {
            var services = new ServiceCollection();
            services.AddDesktopLogging();
            services.AddSingleton<ICurrentUserAccessor, CurrentUserAccessor>();
            services.AddSingleton<IAppSettingsStore, JsonClientSettingsStore>();
            services.AddSingleton<ISecretKeySource, WindowsSecretKeySource>();
            services.AddSingleton<IBackdropCapabilities, WindowsBackdropCapabilities>();
            services
                .AddWebRtc();

            if (PackageEnvironment.IsRunningAsPackaged())
            {
                services.AddSingleton<IUpdateService, StoreUpdateService>();
            }
            else
            {
                services.AddSingleton<IUpdateService, VelopackUpdateService>();
            }

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
