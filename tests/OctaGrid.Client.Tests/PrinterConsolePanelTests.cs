using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Controls;
using OctaGrid.Client.DesignTime;
using OctaGrid.Client.Services;
using OctaGrid.Client.ViewModels;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Tests;

/// <summary>The console panel in a real (headless) window: what it shows, and how it scrolls as lines arrive.</summary>
[Collection(UiCollection.Name)]
public class PrinterConsolePanelTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static (Window Window, PrinterConsolePanel Panel, PrinterConsoleViewModel ViewModel, ScrollViewer Scroller) Show()
    {
        var viewModel = new PrinterConsoleViewModel(Guid.NewGuid(), new DesignAgentCommands());
        var panel = new PrinterConsolePanel { DataContext = viewModel };
        var window = new Window { Content = panel, Width = 800, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var scroller = panel.GetVisualDescendants().OfType<ScrollViewer>().Single();
        return (window, panel, viewModel, scroller);
    }

    private static void AddLines(PrinterConsoleViewModel viewModel, int from, int to)
    {
        for (var i = from; i <= to; i++)
        {
            viewModel.Lines.Add(new ConsoleLineItem(new ConsoleLine(i, Start.AddSeconds(i), $"line {i}")));
        }

        Dispatcher.UIThread.RunJobs();
    }

    private static bool IsAtBottom(ScrollViewer scroller) =>
        scroller.Offset.Y + scroller.Viewport.Height >= scroller.Extent.Height - 1;

    [Fact]
    public async Task Lines_are_shown_with_their_time_and_text()
    {
        await HeadlessUi.Run(() =>
        {
            var (window, panel, viewModel, _) = Show();

            AddLines(viewModel, 1, 3);

            var texts = panel.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("line 1", texts);
            Assert.Contains("line 3", texts);
            window.Close();
        });
    }

    [Fact]
    public async Task The_panel_follows_the_newest_line_while_the_user_is_at_the_bottom()
    {
        await HeadlessUi.Run(() =>
        {
            var (window, _, viewModel, scroller) = Show();

            AddLines(viewModel, 1, 200);
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
            Assert.True(scroller.Extent.Height > scroller.Viewport.Height);
            Assert.True(IsAtBottom(scroller));

            AddLines(viewModel, 201, 230);
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);

            Assert.True(IsAtBottom(scroller));
            window.Close();
        });
    }

    [Fact]
    public async Task The_panel_stays_where_the_user_scrolled_to_when_new_lines_arrive()
    {
        await HeadlessUi.Run(() =>
        {
            var (window, _, viewModel, scroller) = Show();
            AddLines(viewModel, 1, 200);
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);

            scroller.Offset = new Avalonia.Vector(0, 0);
            Dispatcher.UIThread.RunJobs();
            AddLines(viewModel, 201, 205);
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);

            Assert.Equal(0, scroller.Offset.Y);
            window.Close();
        });
    }

    [Fact]
    public async Task A_panel_that_is_gone_no_longer_follows_the_lines()
    {
        await HeadlessUi.Run(() =>
        {
            var (window, panel, viewModel, scroller) = Show();
            AddLines(viewModel, 1, 200);
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);

            // The tab content is created again when the tab is shown again; the old panel leaves the tree.
            window.Content = null;
            Dispatcher.UIThread.RunJobs();
            var offsetBefore = scroller.Offset;
            AddLines(viewModel, 201, 260);
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);

            Assert.Equal(offsetBefore, scroller.Offset);
            Assert.Null(panel.Parent);
            window.Close();
        });
    }
}
