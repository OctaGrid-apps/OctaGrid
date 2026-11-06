using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OctaGrid.Client.Controls;
using OctaGrid.Client.DesignTime;
using OctaGrid.Client.ViewModels;
using OctaGrid.Client.Views;

namespace OctaGrid.Client.Tests;

/// <summary>
/// Creates the real views in a headless Avalonia application, with the designer's sample view models,
/// and checks that pages receive their view model from their owner instead of finding it themselves.
/// </summary>
[Collection(UiCollection.Name)]
public class PageWiringTests
{
    private static Window Show(Control content)
    {
        var window = new Window { Content = content, Width = 1000, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [Fact]
    public async Task The_main_view_shows_its_first_page_with_the_view_model_the_main_view_model_supplies()
    {
        await HeadlessUi.Run(() =>
        {
            var main = DesignData.Main;
            var window = Show(new MainView { DataContext = main });

            var rail = window.GetVisualDescendants().OfType<NavigationRail>().Single();

            var page = Assert.IsType<DashboardsView>(rail.CurrentPage);
            Assert.Same(main.DashboardsPage, page.DataContext);
            window.Close();
        });
    }

    [Fact]
    public async Task Every_navigation_item_creates_its_page_with_the_matching_view_model()
    {
        await HeadlessUi.Run(() =>
        {
            var main = DesignData.Main;
            var window = Show(new MainView { DataContext = main });
            var rail = window.GetVisualDescendants().OfType<NavigationRail>().Single();

            var expected = new Dictionary<Type, object>
            {
                [typeof(DashboardsView)] = main.DashboardsPage,
                [typeof(PrinterListView)] = main.PrintersPage,
                [typeof(AgentListView)] = main.AgentsPage,
                [typeof(WorkspaceListView)] = main.WorkspacesPage,
                [typeof(SettingsView)] = main.SettingsPage,
            };

            var itemsWithPages = rail.Items.Concat(rail.FooterItems).Where(i => i.PageType is not null).ToList();
            Assert.Equal(expected.Count, itemsWithPages.Count);

            foreach (var item in itemsWithPages)
            {
                rail.SelectedItem = item;
                Dispatcher.UIThread.RunJobs();

                var page = rail.CurrentPage;
                Assert.IsType(item.PageType!, page);
                Assert.Same(expected[item.PageType!], page!.DataContext);
            }

            window.Close();
        });
    }

    [Fact]
    public async Task A_page_keeps_working_without_an_application_container()
    {
        await HeadlessUi.Run(() =>
        {
            Assert.Null(App.Services);

            var window = Show(new SettingsView { DataContext = DesignData.SettingsPage });

            Assert.IsType<SettingsViewModel>(((SettingsView)window.Content!).DataContext);
            window.Close();
        });
    }

    [Fact]
    public async Task The_printers_page_loads_the_sample_printers_when_it_is_shown()
    {
        await HeadlessUi.Run(() =>
        {
            var printers = DesignData.Printers;

            var window = Show(new PrinterListView { DataContext = printers });

            // Two sample printers plus the "add printer" tile.
            Assert.Equal(3, printers.Printers.Count);
            window.Close();
        });
    }

    [Fact]
    public async Task The_dashboard_page_loads_a_card_for_each_sample_printer()
    {
        await HeadlessUi.Run(() =>
        {
            var dashboards = DesignData.Dashboards;

            var window = Show(new DashboardsView { DataContext = dashboards });

            Assert.Equal(2, dashboards.Printers.Count);
            window.Close();
        });
    }

    [Fact]
    public async Task The_workspace_and_agent_pages_load_their_sample_data()
    {
        await HeadlessUi.Run(() =>
        {
            var workspaces = DesignData.Workspaces;
            var agents = DesignData.Agents;

            var workspaceWindow = Show(new WorkspaceListView { DataContext = workspaces });
            var agentWindow = Show(new AgentListView { DataContext = agents });

            Assert.NotEmpty(workspaces.Workspaces);
            Assert.NotEmpty(agents.Agents);
            workspaceWindow.Close();
            agentWindow.Close();
        });
    }

    [Fact]
    public async Task A_page_without_a_data_context_does_not_fail_when_shown()
    {
        await HeadlessUi.Run(() =>
        {
            var window = Show(new PrinterListView());

            window.Close();
        });
    }

    [Fact]
    public async Task The_main_view_can_be_shown_again_without_opening_a_dialog()
    {
        // The designer raises Loaded again whenever the preview is rebuilt, for example when it is zoomed.
        await HeadlessUi.Run(() =>
        {
            var main = DesignData.Main;
            var first = Show(new MainView { DataContext = main });
            first.Close();
            var second = Show(new MainView { DataContext = main });

            var overlays = second.GetVisualDescendants().OfType<OctaGrid.Client.Controls.Dialogs.OverlayDialog>();
            Assert.Empty(overlays);
            second.Close();
        });
    }

    [Fact]
    public async Task The_main_view_attaches_notifications_to_its_window()
    {
        await HeadlessUi.Run(() =>
        {
            var main = DesignData.Main;
            var window = Show(new MainView { DataContext = main });

            // Attaching is idempotent and must not throw once the view has a window.
            main.AttachNotifications(window);
            window.Close();
        });
    }
}
