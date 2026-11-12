using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OctaGrid.Client.DesignTime;
using OctaGrid.Client.ViewModels;
using OctaGrid.Client.Views;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Client.Tests;

/// <summary>The printer page at different widths: it follows its own width, not a fixed two-column layout.</summary>
[Collection(UiCollection.Name)]
public class PrinterPageResponsiveTests
{
    private static (Window Window, PrinterPageView View, PrinterPageViewModel Page) Show(double width)
    {
        var page = DesignData.PrinterPage;
        var view = new PrinterPageView { DataContext = page };
        var window = new Window { Content = view, Width = width, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view, page);
    }

    private static T Named<T>(Control root, string name)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    private static void Resize(Window window, double width)
    {
        window.Width = width;
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public async Task A_wide_page_shows_the_job_and_the_temperatures_side_by_side()
    {
        await HeadlessUi.Run(() =>
        {
            var (window, view, _) = Show(1100);
            var job = Named<Border>(view, "JobCard");
            var temperatures = Named<Border>(view, "TemperaturesCard");

            Assert.Equal((0, 0, 1), (Grid.GetRow(job), Grid.GetColumn(job), Grid.GetColumnSpan(job)));
            Assert.Equal((0, 1, 1), (Grid.GetRow(temperatures), Grid.GetColumn(temperatures), Grid.GetColumnSpan(temperatures)));
            Assert.True(temperatures.Bounds.X > job.Bounds.Right - 1);
            window.Close();
        });
    }

    [Fact]
    public async Task A_narrow_page_puts_the_temperatures_below_the_job_and_gives_each_the_full_width()
    {
        await HeadlessUi.Run(() =>
        {
            var (window, view, _) = Show(1100);
            var job = Named<Border>(view, "JobCard");
            var temperatures = Named<Border>(view, "TemperaturesCard");

            Resize(window, 600);

            Assert.Equal((0, 0, 2), (Grid.GetRow(job), Grid.GetColumn(job), Grid.GetColumnSpan(job)));
            Assert.Equal((1, 0, 2), (Grid.GetRow(temperatures), Grid.GetColumn(temperatures), Grid.GetColumnSpan(temperatures)));
            Assert.True(temperatures.Bounds.Y >= job.Bounds.Bottom);
            Assert.Equal(job.Bounds.Width, temperatures.Bounds.Width, 1);
            Assert.True(temperatures.Margin.Top > 0);
            window.Close();
        });
    }

    [Fact]
    public async Task The_page_goes_back_to_two_columns_when_it_is_widened_again()
    {
        await HeadlessUi.Run(() =>
        {
            var (window, view, _) = Show(600);
            var temperatures = Named<Border>(view, "TemperaturesCard");
            Assert.Equal(1, Grid.GetRow(temperatures));

            Resize(window, 1100);

            Assert.Equal((0, 1), (Grid.GetRow(temperatures), Grid.GetColumn(temperatures)));
            Assert.Equal(0, temperatures.Margin.Top);
            window.Close();
        });
    }

    [Theory]
    [InlineData(1100, 2)]
    [InlineData(600, 2)]
    [InlineData(380, 1)]
    public async Task The_temperature_tiles_share_a_row_until_the_page_is_very_narrow(double width, int columns)
    {
        await HeadlessUi.Run(() =>
        {
            var (window, view, _) = Show(width);

            Assert.Equal(columns, Named<UniformGrid>(view, "TemperatureTiles").Columns);
            window.Close();
        });
    }

    [Fact]
    public async Task Nothing_of_the_overview_is_wider_than_the_page_at_a_narrow_width()
    {
        await HeadlessUi.Run(() =>
        {
            var (window, view, _) = Show(400);
            var grid = Named<Grid>(view, "OverviewGrid");

            // The cards share the grid, which has to fit in the window; none of them may push it out.
            Assert.True(grid.Bounds.Width <= window.Width, $"the grid is {grid.Bounds.Width} wide, the window {window.Width}");
            foreach (var card in new[] { Named<Border>(view, "JobCard"), Named<Border>(view, "TemperaturesCard") })
            {
                Assert.True(card.Bounds.Right <= grid.Bounds.Width + 1, $"{card.Name} reaches {card.Bounds.Right}, the grid is {grid.Bounds.Width} wide");
            }

            window.Close();
        });
    }

    [Fact]
    public async Task A_file_row_gives_up_its_estimated_time_on_a_narrow_page()
    {
        await HeadlessUi.Run(() =>
        {
            var (window, view, page) = Show(1100);
            page.SelectedTabIndex = (int)PrinterPageTab.Files;
            var file = new PrintMetadataListItem(new PrintMetadata("benchy.gcode", 1, "PLA", 0.4, 5400), []);
            page.FileRows.Add(new PrinterFileRowViewModel(file, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask, () => true));
            Dispatcher.UIThread.RunJobs();

            var estimate = view.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Classes.Contains("file-estimate"));
            Assert.True(estimate.IsEffectivelyVisible);

            Resize(window, 500);

            Assert.False(estimate.IsEffectivelyVisible);
            window.Close();
        });
    }
}
