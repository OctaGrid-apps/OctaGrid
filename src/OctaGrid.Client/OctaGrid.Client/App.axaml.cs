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

public partial class App : Application
{
    private readonly CancellationTokenSource _cts = new();
    /// <summary>
    /// The application container. Set once at startup and used only by the application root to create
    /// the first view model; it is null in the designer and in tests that build their own view models.
    /// </summary>
    public static IServiceProvider? Services { get; set; }

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

            var provider = services.BuildServiceProvider();
            Services = provider;

            var logger = provider.GetService<ILogger<App>>() ?? NullLogger<App>.Instance;
            logger.LogInformation("Application services registered successfully");
        }
        catch (Exception ex)
        {
            Trace.TraceError(ex.ToString());
            throw;
        }
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        Logger.LogInformation("Application initialized");

#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    private Avalonia.Markup.Xaml.Styling.StyleInclude? _micaTheme;

    /// <summary>
    /// SolidTheme is always loaded. On Windows the Mica theme is layered on top of it while the Mica backdrop
    /// is selected; it makes the window transparent, so it must not stay loaded when the backdrop is turned off.
    /// </summary>
    private void ApplyBackdropTheme(ClientSettings settings)
    {
        var wanted = !Design.IsDesignMode && OperatingSystem.IsWindows() && settings.TransparencyLevel == TransparencyLevel.Mica;
        if (wanted == (_micaTheme is not null))
        {
            return;
        }

        if (wanted)
        {
            _micaTheme = new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://OctaGrid.Client/"))
            {
                Source = new Uri("avares://OctaGrid.Client/Styles/Themes/MicaTheme.axaml")
            };
            Styles.Add(_micaTheme);
        }
        else
        {
            Styles.Remove(_micaTheme!);
            _micaTheme = null;
        }
    }

    private static ILogger Logger => Services?.GetService<ILogger<App>>() ?? NullLogger<App>.Instance;

    public override void OnFrameworkInitializationCompleted()
    {
        // View models are matched to their views by type (see ViewLocator). Pages and dialogs never
        // look their own view model up: whoever shows a view supplies it.
        DataTemplates.Add(new ViewLocator(typeof(App).Assembly));

        // The designer and tests have no container; they create the view models they need directly.
        if (Services is not { } services)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        var logger = Logger;
        logger.LogInformation("Framework initialization started with lifetime: {Lifetime}", ApplicationLifetime?.GetType().Name);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settingsStore = services.GetRequiredService<IAppSettingsStore>();

            // Local mode only. Remote mode is future feature
            settingsStore.Update(s => s with
            {
                Mode = ClientMode.Local,
                AskToSelectClientMode = false
            });

            var settings = settingsStore.ClientSettings;
            logger.LogInformation("Client settings loaded: Mode={Mode}, AskToSelectClientMode={AskToSelectClientMode}", settings.Mode, settings.AskToSelectClientMode);
            ApplyBackdropTheme(settings);

            if (settings.Mode == ClientMode.Undefined)
            {
                logger.LogInformation("Client mode is Undefined, showing ModeSelectionWindow");
                desktop.MainWindow = new ModeSelectionWindow
                {
                    DataContext = services.GetRequiredService<ModeSelectionViewModel>()
                };
            }
            else
            {
                if (settings.AskToSelectClientMode)
                {
                    settingsStore.Update(s => s with { Mode = ClientMode.Undefined });
                }

                // Each mode registers what it needs running: Local hosts the in-process agent,
                // Remote holds the server connection. App stays mode-agnostic.
                foreach (var runtime in services.GetServices<IClientRuntime>())
                {
                    _ = RunRuntimeAsync(runtime, logger, _cts.Token);
                }

                _ = StartDeveloperModeAsync(services, desktop.Args, settings, logger);

                desktop.Exit += (s, e) =>
                {
                    logger.LogInformation("Desktop application exit requested");
                    _cts.Cancel();
                    _cts.Dispose();
                };

                var mainWindow = new MainWindow
                {
                    DataContext = services.GetRequiredService<MainViewModel>()
                };
                settingsStore.SettingsChanged += (_, changed) =>
                {
                    ApplyBackdropTheme(changed);
                    mainWindow.ApplyTheme(changed);
                };
                desktop.MainWindow = mainWindow;
            }

            logger.LogInformation("Applying theme {ElementTheme} to main window", settings.ElementTheme);
            desktop.MainWindow.ApplyTheme(settings);
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
        {
            logger.LogInformation("Configuring activity application lifetime main view factory");
            singleViewFactoryApplicationLifetime.MainViewFactory = () => new MainView
            {
                DataContext = services.GetRequiredService<MainViewModel>()
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            logger.LogInformation("Configuring single view application lifetime main view");
            singleViewPlatform.MainView = new MainView
            {
                DataContext = services.GetRequiredService<MainViewModel>()
            };
        }

        base.OnFrameworkInitializationCompleted();
        logger.LogInformation("Framework initialization completed");
    }

    // Developer mode: the folder comes from --extensions-dev, the environment or the saved setting. Modes that cannot
    // load extensions (no extensions client) simply have nothing to do.
    private static async Task StartDeveloperModeAsync(IServiceProvider services, IReadOnlyList<string>? args, ClientSettings settings, ILogger logger)
    {
        try
        {
            var folder = DeveloperMode.ResolveFolder(args, settings.DeveloperExtensionsFolder);
            if (folder is null || services.GetService<IExtensionsClient>() is not { } extensions)
            {
                return;
            }

            var result = await extensions.SetDeveloperFolderAsync(folder, CancellationToken.None);
            logger.LogInformation("Developer mode for {Folder}: {Success} {Message}", folder, result.Success, result.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start developer mode");
        }
    }

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
