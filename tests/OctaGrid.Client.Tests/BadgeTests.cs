using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OctaGrid.Client.Controls;
using OctaGrid.Client.Dialogs;
using OctaGrid.Client.ViewModels;
using OctaGrid.Protocol.Messaging;

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
    private static void AssertTextIsCentered(Badge badge)
    {
        var text = badge.GetVisualDescendants().OfType<TextBlock>().First(t => !string.IsNullOrEmpty(t.Text));

        Assert.True(double.IsNaN(text.LineHeight), $"'{text.Text}' has a fixed line height of {text.LineHeight}.");

        var above = text.TranslatePoint(new Point(0, 0), badge)!.Value.Y;
        var below = badge.Bounds.Height - above - text.Bounds.Height;
        Assert.True(Math.Abs(above - below) <= 0.5, $"'{text.Text}' sits {above:0.#} px from the top and {below:0.#} px from the bottom.");
    }

    [Fact]
    public async Task The_text_of_a_badge_is_centered_vertically()
    {
        await HeadlessUi.Run(() =>
        {
            var badge = new Badge { Content = "Default" };
            var window = Show(badge);

            AssertTextIsCentered(badge);
            window.Close();
        });
    }

    [Fact]
    public async Task A_badge_keeps_the_caption_text_size_and_is_not_taller_than_its_text_and_padding()
    {
        await HeadlessUi.Run(() =>
        {
            var badge = new Badge { Content = "Active", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
            var window = Show(badge);
            var text = badge.GetVisualDescendants().OfType<TextBlock>().First();

            Assert.Equal(12, text.FontSize);
            Assert.Equal(text.Bounds.Height + 4, badge.Bounds.Height, 0.5);
            window.Close();
        });
    }

    [Fact]
    public async Task The_extension_dialog_shows_its_states_in_centered_badges()
    {
        await HeadlessUi.RunAsync(async () =>
        {
            var viewModel = new ExtensionsViewModel(new OctaGrid.Client.DesignTime.DesignExtensionsClient());
            await viewModel.LoadCommand.ExecuteAsync(null);
            var window = Show(new ExtensionsView { DataContext = viewModel });

            var badges = window.GetVisualDescendants().OfType<Badge>().ToList();
            Assert.Equal(viewModel.Items.Count, badges.Count);
            Assert.All(badges, AssertTextIsCentered);
            window.Close();
        });
    }

    [Fact]
    public async Task The_dashboard_dialog_shows_the_default_in_a_centered_badge()
    {
        await HeadlessUi.Run(() =>
        {
            var client = new OctaGrid.Client.DesignTime.DesignOctaGridClient();
            var dashboards = client.GetDashboardList(OctaGrid.Client.DesignTime.DesignOctaGridClient.DefaultWorkspaceId, CancellationToken.None).Result;
            var window = Show(new ManageDashboardsView
            {
                DataContext = new ManageDashboardsViewModel(client, OctaGrid.Client.DesignTime.DesignOctaGridClient.DefaultWorkspaceId, dashboards),
            });

            var badge = Assert.Single(window.GetVisualDescendants().OfType<Badge>(), b => b.IsEffectivelyVisible);
            Assert.Equal("Default", badge.Content);
            AssertTextIsCentered(badge);
            window.Close();
        });
    }

    [Fact]
    public async Task The_printer_state_badge_uses_the_same_control_and_centers_its_text()
    {
        await HeadlessUi.Run(() =>
        {
            var state = new PrinterStateBadge { State = UniversalPrinterState.Offline };
            var window = Show(state);

            AssertTextIsCentered(Assert.Single(state.GetVisualDescendants().OfType<Badge>()));
            window.Close();
        });
    }
}
