using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OctaGrid.Client.Controls;
using OctaGrid.Client.DesignTime;
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
    public async Task The_printers_page_is_kept_and_raises_Loaded_each_time_it_is_shown()
    {
        // The page reloads its data on Loaded. That is how the list notices, when the user comes back from the
        // Settings page, that a connector extension was turned on or off.
        await HeadlessUi.Run(() =>
        {
            var window = Show(new MainView { DataContext = DesignData.Main });
            var rail = window.GetVisualDescendants().OfType<NavigationRail>().Single();
            var items = rail.Items.Concat(rail.FooterItems).ToList();
            var printers = items.Single(i => i.PageType == typeof(PrinterListView));
            var settings = items.Single(i => i.PageType == typeof(SettingsView));

            rail.SelectedItem = printers;
            Dispatcher.UIThread.RunJobs();
            var page = rail.CurrentPage!;
            var loaded = 0;
            page.Loaded += (_, _) => loaded++;

            rail.SelectedItem = settings;
            Dispatcher.UIThread.RunJobs();
            rail.SelectedItem = printers;
            Dispatcher.UIThread.RunJobs();

            Assert.Same(page, rail.CurrentPage);
            Assert.Equal(1, loaded);
            window.Close();
        });
    }
}
