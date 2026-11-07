using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OctaGrid.Client.Controls;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;
using OctaGrid.Client.ViewModels;
using OctaGrid.Client.Views;

namespace OctaGrid.Client.Tests;

/// <summary>
/// Renders the Settings page with the real view in each state of an update and checks the Updates card: one button in
/// the header at a time, the details in rows of their own, and nothing clipped or squeezed out at narrow widths.
/// </summary>
[Collection(UiCollection.Name)]
public class UpdateCardTests
{
    private sealed class FakeUpdateService : IUpdateService
    {
        private readonly TaskCompletionSource _downloadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public UpdateChannel Channel => UpdateChannel.Velopack;

        public bool IsUpdateSupported => true;

        public int ReportProgress { get; set; } = 40;

        public Task DownloadStarted => _downloadStarted.Task;

        public Task<AppUpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AppUpdateInfo(true, "1.0.0", "1.1.0", "<p>Notes</p>", null));

        public async Task DownloadAndApplyUpdateAsync(AppUpdateInfo updateInfo, Action<int>? progressCallback, CancellationToken cancellationToken)
        {
            progressCallback?.Invoke(ReportProgress);
            _downloadStarted.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        public Task OpenStoreListingAsync() => Task.CompletedTask;
    }

    public static TheoryData<double> Widths => [360, 480, 700, 1100];

    private static (SettingsViewModel ViewModel, FakeUpdateService Service) Create(bool updateAvailable)
    {
        var service = new FakeUpdateService();
        var state = new ApplicationState(new InMemorySettingsStore());
        if (updateAvailable)
        {
            state.AvailableUpdate = new AppUpdateInfo(true, "1.0.0", "1.1.0", "<p>Notes</p>", null);
        }

        return (new SettingsViewModel(new InMemorySettingsStore(), state, service), service);
    }

    private static Window Show(SettingsViewModel viewModel, double width)
    {
        var window = new Window { Content = new SettingsView { DataContext = viewModel }, Width = width, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Container queries switch on after the first layout, which can change the layout again.
        for (var pass = 0; pass < 3; pass++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        return window;
    }

    private static SettingsExpander Card(Window window) =>
        window.GetVisualDescendants().OfType<SettingsExpander>().Single(e => e.Header == "Application updates");

    private static Border HeaderOf(SettingsExpander card) =>
        card.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_HeaderBorder");

    private static ContentPresenter ActionOf(SettingsExpander card) =>
        card.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "PART_ActionPresenter");

    private static List<string> HeaderButtons(SettingsExpander card) =>
        ActionOf(card).GetVisualDescendants().OfType<Button>()
            .Where(b => b.IsEffectivelyVisible)
            .Select(b => b.Content?.ToString() ?? string.Empty)
            .ToList();

    // The header must show everything: the button is whole, inside the card, and the text next to it keeps room.
    private static void AssertHeaderFits(SettingsExpander card)
    {
        var header = HeaderOf(card);
        var action = ActionOf(card);
        var text = card.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.IsEffectivelyVisible && t.Classes.Contains("caption") && t.Classes.Contains("secondary"));

        foreach (var button in action.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible))
        {
            Assert.True(button.Bounds.Width + 0.5 >= button.DesiredSize.Width, $"'{button.Content}' is clipped.");

            var topLeft = button.TranslatePoint(new Point(0, 0), header)!.Value;
            Assert.True(topLeft.X >= 0 && topLeft.X + button.Bounds.Width <= header.Bounds.Width + 0.5, $"'{button.Content}' sticks out of the card.");
        }

        // Side by side the text ends where the button begins; in a narrow card the button sits under the text.
        var textOrigin = text.TranslatePoint(new Point(0, 0), header)!.Value;
        var actionOrigin = action.TranslatePoint(new Point(0, 0), header)!.Value;
        var besideEachOther = textOrigin.X + text.Bounds.Width <= actionOrigin.X + 0.5;
        var underneath = actionOrigin.Y >= textOrigin.Y + text.Bounds.Height - 0.5;
        Assert.True(besideEachOther || underneath, "The status text and the button overlap.");
        Assert.True(text.Bounds.Width >= 60, $"The status text is squeezed to {text.Bounds.Width:0} px.");

        if (header.Bounds.Width <= 480)
        {
            Assert.True(underneath, "In a narrow card the button should sit under the text.");
        }
    }

    [Theory]
    [MemberData(nameof(Widths))]
    public async Task With_nothing_to_install_the_header_has_one_check_button(double width)
    {
        await HeadlessUi.RunAsync(() =>
        {
            var (viewModel, _) = Create(updateAvailable: false);
            var window = Show(viewModel, width);
            var card = Card(window);

            Assert.Equal(["Check for updates"], HeaderButtons(card));
            Assert.False(card.IsExpanded);
            AssertHeaderFits(card);
            window.Close();
            return Task.CompletedTask;
        });
    }

    [Theory]
    [MemberData(nameof(Widths))]
    public async Task With_an_update_waiting_the_header_has_one_update_button_and_the_card_is_highlighted(double width)
    {
        await HeadlessUi.RunAsync(() =>
        {
            var (viewModel, _) = Create(updateAvailable: true);
            var window = Show(viewModel, width);
            var card = Card(window);

            Assert.Equal(["Update and restart"], HeaderButtons(card));
            Assert.Contains("highlight", card.Classes);
            AssertHeaderFits(card);
            window.Close();
            return Task.CompletedTask;
        });
    }

    [Theory]
    [MemberData(nameof(Widths))]
    public async Task While_downloading_the_card_opens_with_progress_and_the_header_has_only_cancel(double width)
    {
        await HeadlessUi.RunAsync(async () =>
        {
            var (viewModel, service) = Create(updateAvailable: true);
            var window = Show(viewModel, width);
            var card = Card(window);

            var update = viewModel.ApplyUpdateCommand.ExecuteAsync(null);
            await service.DownloadStarted;
            Dispatcher.UIThread.RunJobs();

            Assert.True(card.IsExpanded, "The card should open by itself when the update starts.");
            Assert.Equal(["Cancel"], HeaderButtons(card));
            AssertHeaderFits(card);

            var bar = card.GetVisualDescendants().OfType<ProgressBar>().Single(p => p.IsEffectivelyVisible);
            Assert.Equal(40, bar.Value);
            Assert.False(bar.IsIndeterminate);
            Assert.True(bar.Bounds.Width > 100, "The progress bar should have a row of its own.");
            Assert.Contains("40%", card.Description);

            viewModel.ApplyUpdateCancelCommand.Execute(null);
            await update;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(["Update and restart"], HeaderButtons(card));
            window.Close();
        });
    }

    [Fact]
    public async Task Once_the_download_is_complete_the_header_has_no_button_that_could_do_nothing()
    {
        await HeadlessUi.RunAsync(async () =>
        {
            var (viewModel, service) = Create(updateAvailable: true);
            service.ReportProgress = 100;
            var window = Show(viewModel, 600);
            var card = Card(window);

            var update = viewModel.ApplyUpdateCommand.ExecuteAsync(null);
            await service.DownloadStarted;
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(HeaderButtons(card));
            Assert.False(viewModel.CanCancelUpdate);

            // The fake never finishes by itself; stop it through the token.
            viewModel.ApplyUpdateCancelCommand.Execute(null);
            window.Close();
            _ = update;
        });
    }

    [Theory]
    [InlineData(360)]
    [InlineData(480)]
    [InlineData(1100)]
    public async Task The_rows_inside_the_open_card_keep_their_text_and_their_buttons_whole(double width)
    {
        await HeadlessUi.RunAsync(() =>
        {
            var (viewModel, _) = Create(updateAvailable: true);
            var window = Show(viewModel, width);
            var card = Card(window);
            card.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var rows = card.GetVisualDescendants().OfType<SettingsCard>().Where(c => c.IsEffectivelyVisible).ToList();
            Assert.NotEmpty(rows);

            foreach (var row in rows)
            {
                foreach (var button in row.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible))
                {
                    var origin = button.TranslatePoint(new Point(0, 0), row)!.Value;
                    Assert.True(button.Bounds.Width + 0.5 >= button.DesiredSize.Width, $"'{button.Content}' is clipped at {width} px.");
                    Assert.True(origin.X + button.Bounds.Width <= row.Bounds.Width + 0.5, $"'{button.Content}' sticks out of its row at {width} px.");
                }

                foreach (var text in row.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text) && t.FindAncestorOfType<Button>() is null))
                {
                    Assert.True(text.Bounds.Width >= 60, $"'{text.Text}' is squeezed to {text.Bounds.Width:0} px at {width} px.");
                }
            }

            window.Close();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task The_details_are_rows_of_their_own_inside_the_card()
    {
        await HeadlessUi.RunAsync(() =>
        {
            var (viewModel, _) = Create(updateAvailable: true);
            var window = Show(viewModel, 600);
            var card = Card(window);
            card.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();

            var rows = card.GetVisualDescendants().OfType<SettingsCard>().Where(c => c.IsEffectivelyVisible).ToList();
            var headers = rows.Select(r => r.Header as string).Where(h => h is not null).ToList();

            Assert.Contains("Release notes", headers);
            Assert.Contains("Check for updates", headers);
            Assert.Contains("Update channel", headers);
            Assert.Contains("When the update is installed", headers);

            // The rows sit one under the other, so no two overlap.
            var tops = rows.Select(r => r.TranslatePoint(new Point(0, 0), card)!.Value.Y).ToList();
            Assert.Equal(tops.Order(), tops);
            for (var i = 1; i < rows.Count; i++)
            {
                Assert.True(tops[i] >= tops[i - 1] + rows[i - 1].Bounds.Height - 0.5, $"Row {i} overlaps row {i - 1}.");
            }

            window.Close();
            return Task.CompletedTask;
        });
    }
}
