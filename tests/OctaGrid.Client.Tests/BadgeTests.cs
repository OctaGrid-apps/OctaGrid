using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OctaGrid.Client.Controls;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Client.Tests;

[Collection(UiCollection.Name)]
public class BadgeTests
{
    private static Window Show(Control content)
    {
        var window = new Window { Content = content, Width = 400, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return window;
    }

    // A line taller than the text leaves the spare space under it, which pushes the text to the top of the badge.
    [Fact]
    public async Task The_text_of_a_badge_follows_the_text_height_instead_of_a_fixed_line_height()
    {
        await HeadlessUi.Run(() =>
        {
            var badge = new Badge { Content = "Default" };
            var window = Show(badge);

            var text = badge.GetVisualDescendants().OfType<TextBlock>().First();
            Assert.True(double.IsNaN(text.LineHeight), $"The badge text has a fixed line height of {text.LineHeight}.");
            window.Close();
        });
    }

    [Fact]
    public async Task The_printer_state_badge_is_a_badge()
    {
        await HeadlessUi.Run(() =>
        {
            var state = new PrinterStateBadge { State = UniversalPrinterState.Offline };
            var window = Show(state);

            Assert.Single(state.GetVisualDescendants().OfType<Badge>());
            window.Close();
        });
    }
}
