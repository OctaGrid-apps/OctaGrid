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
            viewModel.Lines.Insert(0, new ConsoleLineItem(new ConsoleLine(i, Start.AddSeconds(i), $"line {i}")));
        }

        Dispatcher.UIThread.RunJobs();
    }


    [Fact]
    public async Task A_risky_command_is_not_sent_unless_the_user_confirms()
    {
        // Showing the question needs a window to put it in; the panel is not in one here, so the question cannot be
        // answered, which counts as "no".
        await HeadlessUi.Run(async () =>
        {
            var commands = new PrinterConsoleViewModelTests.FakeCommands();
            var viewModel = new PrinterConsoleViewModel(Guid.NewGuid(), commands, supportsCommands: true)
            {
                IsExpertMode = true,
                CommandText = "FIRMWARE_RESTART",
            };

            await Assert.ThrowsAnyAsync<Exception>(() => viewModel.SendCommand.ExecuteAsync(null));

            Assert.Empty(commands.Sent);
            Assert.Equal("FIRMWARE_RESTART", viewModel.CommandText);
        });
    }

    [Fact]
    public async Task The_copy_button_puts_the_lines_on_the_clipboard_and_the_lines_can_be_selected()
    {
        await HeadlessUi.Run(async () =>
        {
            var (window, panel, viewModel, _) = Show();
            AddLines(viewModel, 1, 3);

            var copy = panel.GetVisualDescendants().OfType<Button>()
                .Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Copy console text");
            copy.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            var clipboard = window.Clipboard;
            Assert.NotNull(clipboard);
            var text = await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(clipboard);
            Assert.Equal(viewModel.GetTranscript(), text);
            Assert.Contains("line 1", text, StringComparison.Ordinal);

            // Each line is a text block the user can select and copy with Ctrl+C.
            Assert.Equal(3, panel.GetVisualDescendants().OfType<SelectableTextBlock>().Count());
            window.Close();
        });
    }

    [Fact]
    public async Task The_command_box_is_shown_only_when_commands_can_be_sent()
    {
        await HeadlessUi.Run(() =>
        {
            var (window, panel, viewModel, _) = Show();

            // A printer that cannot take commands gets neither the box nor the hint: nothing to explain, nothing to nag about.
            Assert.Empty(CommandBoxes(panel));
            Assert.False(ExpertModeHint(panel).IsEffectivelyVisible);

            // The panel's view model is fixed; a console that supports commands is a different one.
            var capable = new PrinterConsoleViewModel(Guid.NewGuid(), new DesignAgentCommands(), supportsCommands: true);
            panel.DataContext = capable;
            Dispatcher.UIThread.RunJobs();
            Assert.True(capable.ShowExpertModeHint);
            Assert.Empty(CommandBoxes(panel));
            Assert.True(ExpertModeHint(panel).IsEffectivelyVisible);

            capable.IsExpertMode = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Single(CommandBoxes(panel));
            Assert.False(ExpertModeHint(panel).IsEffectivelyVisible);

            capable.IsExpertMode = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(CommandBoxes(panel));
            Assert.True(ExpertModeHint(panel).IsEffectivelyVisible);
            Assert.NotNull(viewModel);
            window.Close();
        });
    }

    private static Control ExpertModeHint(PrinterConsolePanel panel) => panel.FindControl<Control>("ExpertModeHint")!;

    private static List<TextBox> CommandBoxes(PrinterConsolePanel panel) =>
        panel.GetVisualDescendants().OfType<TextBox>().Where(t => t.IsEffectivelyVisible).ToList();


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
    public async Task The_newest_line_is_at_the_top_and_stays_in_view_while_the_user_is_at_the_top()
    {
        await HeadlessUi.Run(() =>
        {
            var (window, panel, viewModel, scroller) = Show();

            AddLines(viewModel, 1, 200);
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
            Assert.True(scroller.Extent.Height > scroller.Viewport.Height);
            Assert.Equal(0, scroller.Offset.Y);

            AddLines(viewModel, 201, 230);
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);

            Assert.Equal(0, scroller.Offset.Y);
            var firstLine = panel.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text?.StartsWith("line ", StringComparison.Ordinal) == true);
            Assert.Equal("line 230", firstLine.Text);
            window.Close();
        });
    }

    [Fact]
    public async Task What_the_user_is_reading_stays_in_place_when_new_lines_are_added_above_it()
    {
        await HeadlessUi.Run(() =>
        {
            var (window, _, viewModel, scroller) = Show();
            AddLines(viewModel, 1, 200);
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);

            scroller.Offset = new Avalonia.Vector(0, 300);
            Dispatcher.UIThread.RunJobs();
            var offsetBefore = scroller.Offset.Y;
            var extentBefore = scroller.Extent.Height;

            // Several lines before the next layout: the view has to move by all of them, not by the first one only.
            AddLines(viewModel, 201, 205);
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);

            var added = scroller.Extent.Height - extentBefore;
            Assert.True(added > 0);
            Assert.Equal(offsetBefore + added, scroller.Offset.Y, 1);
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
            scroller.Offset = new Avalonia.Vector(0, 300);
            Dispatcher.UIThread.RunJobs();

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
