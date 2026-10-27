using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
            services.AddLogging(builder => builder.AddConsole());
            services.AddSingleton<IClientMessageHandler>(new MessageHandler());
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
            services.AddTransient<FilePickerViewModel>();
            services.AddTransient<SettingsViewModel>();

            Services = services.BuildServiceProvider();
        }
        catch (Exception ex)
        {
            // Log to standard trace listeners (Debug window in IDE, or debugView.exe)
            Trace.TraceError(ex.ToString());
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

#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
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

            if (settings.Mode == ClientMode.Undefined)
            {
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
                    desktop.MainWindow = new IdentityView();
                }
                // =============

                if (settings.AskToSelectClientMode)
                {
                    settingsStore.Update(s => s with { Mode = ClientMode.Undefined });
                }

                // Each mode registers what it needs running: Local hosts the in-process agent,
                // Remote holds the server connection. App stays mode-agnostic.
                var logger = Services.GetRequiredService<ILogger<App>>();
                foreach (var runtime in Services.GetServices<IClientRuntime>())
                {
                    _ = RunRuntimeAsync(runtime, logger, _cts.Token);
                }

                desktop.Exit += (s, e) =>
                {
                    _cts.Cancel();
                    _cts.Dispose();
                };

                desktop.MainWindow = new MainWindow();
            }
            desktop.MainWindow.ApplyTheme(settings);
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
        {
            singleViewFactoryApplicationLifetime.MainViewFactory = () => new MainView();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView();
        }

        var locator = new ViewLocator(typeof(App).Assembly);
        DataTemplates.Add(locator);

        base.OnFrameworkInitializationCompleted();
    }

    // Fire-and-forget still needs its failures observed, otherwise a dead agent/connection is silent.
    private static async Task RunRuntimeAsync(IClientRuntime runtime, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            await runtime.StartAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Client runtime {Runtime} stopped unexpectedly", runtime.GetType().Name);
        }
    }
}
