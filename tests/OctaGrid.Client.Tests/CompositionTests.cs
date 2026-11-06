using Microsoft.Extensions.DependencyInjection;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.DesignTime;
using OctaGrid.Client.Services;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Tests;

/// <summary>
/// Builds the application container the way the app does and resolves the view models from it.
/// A missing registration or a constructor the container cannot satisfy shows up here instead of at startup.
/// </summary>
[Collection(UiCollection.Name)]
public sealed class CompositionTests : IDisposable
{
    public void Dispose() => App.Services = null;

    private static IServiceProvider BuildContainer()
    {
        var client = new DesignOctaGridClient();
        var services = new ServiceCollection();

        // What the head project (Desktop) and the mode (Local) normally register.
        services.AddSingleton<IUsersClient>(client);
        services.AddSingleton<IWorkspacesClient>(client);
        services.AddSingleton<IDashboardsClient>(client);
        services.AddSingleton<IPrintersClient>(client);
        services.AddSingleton<IAgentsClient>(client);
        services.AddSingleton<IAuthClient>(client);
        services.AddSingleton<IAppSettingsStore, InMemorySettingsStore>();
        services.AddSingleton<IAgentCommandsService, DesignAgentCommands>();
        services.AddSingleton<IExtensionsClient, DesignExtensionsClient>();
        services.AddSingleton<IUpdateService, NullUpdateService>();

        App.RegisterAppServices(services);
        return App.Services!;
    }

    [Fact]
    public void The_main_view_model_gets_every_page_from_the_container()
    {
        var main = BuildContainer().GetRequiredService<MainViewModel>();

        Assert.NotNull(main.DashboardsPage);
        Assert.NotNull(main.PrintersPage);
        Assert.NotNull(main.AgentsPage);
        Assert.NotNull(main.WorkspacesPage);
        Assert.NotNull(main.SettingsPage);
    }

    [Fact]
    public void The_pages_are_separate_instances_for_every_main_view_model()
    {
        var provider = BuildContainer();

        var first = provider.GetRequiredService<MainViewModel>();
        var second = provider.GetRequiredService<MainViewModel>();

        Assert.NotSame(first.PrintersPage, second.PrintersPage);
    }

    [Fact]
    public void The_settings_page_can_manage_extensions()
    {
        var settings = BuildContainer().GetRequiredService<SettingsViewModel>();

        Assert.True(settings.CanManageExtensions);
    }

    [Theory]
    [InlineData(typeof(ModeSelectionViewModel))]
    [InlineData(typeof(IdentityViewModel))]
    [InlineData(typeof(SafetyDisclaimerViewModel))]
    [InlineData(typeof(DashboardsViewModel))]
    [InlineData(typeof(PrinterListViewModel))]
    [InlineData(typeof(AgentListViewModel))]
    [InlineData(typeof(WorkspaceListViewModel))]
    public void Every_registered_view_model_can_be_created(Type viewModelType) =>
        Assert.NotNull(BuildContainer().GetRequiredService(viewModelType));
}
