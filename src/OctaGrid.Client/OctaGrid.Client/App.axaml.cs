using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;
using OctaGrid.Client.ViewModels;
using OctaGrid.Client.Views;

namespace OctaGrid.Client;

// Fully-qualified: OctaGrid.Application (the renamed shared business-logic project) is a sibling
// namespace under OctaGrid, which shadows the unqualified "Application" name Avalonia expects here.
public partial class App : Application
{
    private readonly CancellationTokenSource _cts = new();
    public static IServiceProvider Services { get; set; } = null!;

    public static void RegisterAppServices(IServiceCollection services)
    {
        try
        {
            services.AddLogging();
            services.AddSingleton<IClientMessageHandler, MessageHandler>();
            services.AddSingleton<INotificationService, NotificationService>();
            services.AddSingleton<IByteArrayImageCache, ByteArrayImageCache>();
            services.AddSingleton<ApplicationState>();

            // ViewModels: registered here so Views resolve them from the container instead of
            // `new`-ing them up, which is what lets each ViewModel declare its real dependencies
            // via its own constructor instead of reaching into App.Services itself.
            services.AddTransient<ModeSelectionViewModel>();
            services.AddTransient<MainViewModel>();
            services.AddTransient<AgentListViewModel>();
            services.AddTransient<PrinterListViewModel>();
            services.AddTransient<WorkspaceListViewModel>();
            services.AddTransient<IdentityViewModel>();
            services.AddTransient<DashboardsViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<EditDashboardViewModel>();
            services.AddTransient<SafetyDisclaimerViewModel>();

            Services = services.BuildServiceProvider();

            var logger = Services.GetService<ILogger<App>>() ?? NullLogger<App>.Instance;
            logger.LogInformation("Application services registered successfully");
        }
        catch (Exception ex)
        {
            // Log to standard trace listeners (Debug window in IDE, or debugView.exe)
            Trace.TraceError(ex.ToString());
            throw;
        }
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        if (!Design.IsDesignMode && OperatingSystem.IsWindows() && Styles.Count > 0)
        {
            var micaTheme = new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://OctaGrid.Client/"))
            {
                Source = new Uri("avares://OctaGrid.Client/Styles/Themes/MicaTheme.axaml")
            };
            Styles.Add(micaTheme);
        }

        if (Design.IsDesignMode)
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton<DesignOctaGridClient>();
            serviceCollection.AddSingleton<IUsersClient>(sp => sp.GetRequiredService<DesignOctaGridClient>());
            serviceCollection.AddSingleton<IWorkspacesClient>(sp => sp.GetRequiredService<DesignOctaGridClient>());
            serviceCollection.AddSingleton<IDashboardsClient>(sp => sp.GetRequiredService<DesignOctaGridClient>());
            serviceCollection.AddSingleton<IPrintersClient>(sp => sp.GetRequiredService<DesignOctaGridClient>());
            serviceCollection.AddSingleton<IAgentsClient>(sp => sp.GetRequiredService<DesignOctaGridClient>());
            serviceCollection.AddSingleton<IAuthClient>(sp => sp.GetRequiredService<DesignOctaGridClient>());
            serviceCollection.AddSingleton<IAppSettingsStore, DesignSettingsStore>();
            serviceCollection.AddSingleton<IAgentCommandsService, AgentCommandsService>();
            RegisterAppServices(serviceCollection);
            Services.GetRequiredService<ApplicationState>().SelectedWorkspace = new Workspace
            {
                Id = DesignOctaGridClient.DefaultWorkspaceId,
                Name = "Default Workspace"
            };
        }

        var logger = Services.GetService<ILogger<App>>() ?? NullLogger<App>.Instance;
        logger.LogInformation("Application initialized");

#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var logger = Services.GetService<ILogger<App>>() ?? NullLogger<App>.Instance;
        logger.LogInformation("Framework initialization started with lifetime: {Lifetime}", ApplicationLifetime?.GetType().Name);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settingsStore = Services.GetRequiredService<IAppSettingsStore>();

            // Local mode only. Remote mode is future feature
            settingsStore.Update(s => s with
            {
                Mode = ClientMode.Local,
                AskToSelectClientMode = false
            });

            var settings = settingsStore.ClientSettings;
            logger.LogInformation("Client settings loaded: Mode={Mode}, AskToSelectClientMode={AskToSelectClientMode}", settings.Mode, settings.AskToSelectClientMode);

            if (settings.Mode == ClientMode.Undefined)
            {
                logger.LogInformation("Client mode is Undefined, showing ModeSelectionWindow");
                desktop.MainWindow = new ModeSelectionWindow();
            }
            else
            {
                // Identity
                // ToDo - improve identity logic
                var usersClient = Services.GetRequiredService<IUsersClient>();
                var userResponse = usersClient.Me(CancellationToken.None).GetAwaiter().GetResult();
                if (userResponse is null)
                {
                    logger.LogInformation("No authenticated user found, showing IdentityView");
                    desktop.MainWindow = new IdentityView();
                }
                else
                {
                    logger.LogInformation("Authenticated user found ({UserId}), preparing MainWindow", userResponse.Id);
                }
                // =============

                if (settings.AskToSelectClientMode)
                {
                    settingsStore.Update(s => s with { Mode = ClientMode.Undefined });
                }

                // Each mode registers what it needs running: Local hosts the in-process agent,
                // Remote holds the server connection. App stays mode-agnostic.
                foreach (var runtime in Services.GetServices<IClientRuntime>())
                {
                    _ = RunRuntimeAsync(runtime, logger, _cts.Token);
                }

                desktop.Exit += (s, e) =>
                {
                    logger.LogInformation("Desktop application exit requested");
                    _cts.Cancel();
                    _cts.Dispose();
                };

                desktop.MainWindow ??= new MainWindow();
            }
            logger.LogInformation("Applying theme {ElementTheme} to main window", settings.ElementTheme);
            desktop.MainWindow.ApplyTheme(settings);
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
        {
            logger.LogInformation("Configuring activity application lifetime main view factory");
            singleViewFactoryApplicationLifetime.MainViewFactory = () => new MainView();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            logger.LogInformation("Configuring single view application lifetime main view");
            singleViewPlatform.MainView = new MainView();
        }

        var locator = new ViewLocator(typeof(App).Assembly);
        DataTemplates.Add(locator);

        base.OnFrameworkInitializationCompleted();
        logger.LogInformation("Framework initialization completed");
    }

    // Fire-and-forget still needs its failures observed, otherwise a dead agent/connection is silent.
    private static async Task RunRuntimeAsync(IClientRuntime runtime, ILogger logger, CancellationToken cancellationToken)
    {
        var runtimeName = runtime.GetType().Name;
        try
        {
            logger.LogInformation("Client runtime {Runtime} starting", runtimeName);
            await runtime.StartAsync(cancellationToken);
            logger.LogInformation("Client runtime {Runtime} finished", runtimeName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Client runtime {Runtime} stopped due to cancellation", runtimeName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Client runtime {Runtime} stopped unexpectedly", runtimeName);
        }
    }
}
