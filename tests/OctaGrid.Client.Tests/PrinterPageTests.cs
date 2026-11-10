using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OctaGrid.Client.Controls;
using OctaGrid.Client.DesignTime;
using OctaGrid.Client.ViewModels;
using OctaGrid.Client.Views;

namespace OctaGrid.Client.Tests;

/// <summary>Opening a printer's page from its dashboard card, and getting back to the page the user came from.</summary>
[Collection(UiCollection.Name)]
public class PrinterPageTests
{
    private static Window Show(Control content)
    {
        var window = new Window { Content = content, Width = 1000, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [Fact]
    public async Task Open_page_on_the_card_shows_the_printer_page_over_the_current_page()
    {
        await HeadlessUi.Run(() =>
        {
            var main = DesignData.Main;
            var window = Show(new MainView { DataContext = main });
            var rail = window.GetVisualDescendants().OfType<NavigationRail>().Single();
            var current = rail.CurrentPage;
            var card = DesignData.PrinterPage.Card;

            card.OpenPageCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            var page = Assert.IsType<PrinterPageViewModel>(main.PrinterPage);
            Assert.Same(card, page.Card);
            Assert.Same(page, rail.DetailContent);
            Assert.Single(window.GetVisualDescendants().OfType<PrinterPageView>());
            Assert.Same(current, rail.CurrentPage);
            window.Close();
        });
    }

    [Fact]
    public async Task Back_closes_the_printer_page_and_keeps_the_page_the_user_came_from()
    {
        await HeadlessUi.Run(() =>
        {
            var main = DesignData.Main;
            var window = Show(new MainView { DataContext = main });
            var rail = window.GetVisualDescendants().OfType<NavigationRail>().Single();
            var current = rail.CurrentPage;

            DesignData.PrinterPage.Card.OpenPageCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            main.PrinterPage!.GoBackCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Null(main.PrinterPage);
            Assert.Null(rail.DetailContent);
            Assert.Same(current, rail.CurrentPage);
            window.Close();
        });
    }

    [Fact]
    public async Task Choosing_another_item_in_the_rail_closes_the_printer_page()
    {
        await HeadlessUi.Run(() =>
        {
            var main = DesignData.Main;
            var window = Show(new MainView { DataContext = main });
            var rail = window.GetVisualDescendants().OfType<NavigationRail>().Single();

            DesignData.PrinterPage.Card.OpenPageCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            rail.SelectedItem = rail.Items.Single(i => i.PageType == typeof(PrinterListView));
            Dispatcher.UIThread.RunJobs();

            Assert.Null(main.PrinterPage);
            Assert.Null(rail.DetailContent);
            window.Close();
        });
    }
}
