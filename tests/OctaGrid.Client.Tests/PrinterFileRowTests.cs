using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OctaGrid.Client.Controls;
using OctaGrid.Client.ViewModels;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Client.Tests;

/// <summary>A file row of the printer page in a real (headless) window: the buttons do what the row's view model says.</summary>
[Collection(UiCollection.Name)]
public class PrinterFileRowTests
{
    private static (Window Window, PrinterFileRow Control, PrinterFileRowViewModel Row, List<string> Printed, List<string> Deleted) Show(
        bool canPrint = true)
    {
        var printed = new List<string>();
        var deleted = new List<string>();
        var file = new PrintMetadataListItem(new PrintMetadata("benchy.gcode", 1024, "PLA", 0.4, 5400), []);
        var row = new PrinterFileRowViewModel(
            file,
            (f, _) =>
            {
                printed.Add(f.Metadata.FileName);
                return Task.CompletedTask;
            },
            (f, _) =>
            {
                deleted.Add(f.Metadata.FileName);
                return Task.CompletedTask;
            },
            () => canPrint);
        var control = new PrinterFileRow { DataContext = row };
        var window = new Window { Content = control, Width = 900, Height = 200 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, control, row, printed, deleted);
    }

    private static Button ButtonNamed(Control control, string name) =>
        control.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == name);

    [Fact]
    public async Task The_print_button_prints_that_file()
    {
        await HeadlessUi.Run(async () =>
        {
            var (window, control, _, printed, _) = Show();
            var print = ButtonNamed(control, "Print benchy.gcode");

            Assert.True(print.IsEffectivelyEnabled);
            await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)print.Command!).ExecuteAsync(null);

            Assert.Equal(["benchy.gcode"], printed);
            window.Close();
        });
    }

    [Fact]
    public async Task The_print_button_is_off_while_the_printer_cannot_start_a_print()
    {
        await HeadlessUi.Run(() =>
        {
            var (window, control, _, _, _) = Show(canPrint: false);

            Assert.False(ButtonNamed(control, "Print benchy.gcode").IsEffectivelyEnabled);
            window.Close();
        });
    }

    [Fact]
    public async Task The_more_menu_offers_delete_for_that_file()
    {
        await HeadlessUi.Run(async () =>
        {
            var (window, control, row, _, deleted) = Show();
            var more = ButtonNamed(control, "More actions");

            more.Flyout!.ShowAt(more);
            Dispatcher.UIThread.RunJobs();

            var delete = ((MenuFlyout)more.Flyout).Items.OfType<MenuItem>().Single();
            Assert.Equal("Delete", delete.Header);
            Assert.Same(row.DeleteCommand, delete.Command);

            await row.DeleteCommand.ExecuteAsync(null);
            Assert.Equal(["benchy.gcode"], deleted);
            window.Close();
        });
    }

    [Fact]
    public async Task A_delete_that_fails_is_reported_to_the_user_and_does_not_escape_from_the_menu()
    {
        // Asking "delete it?" needs a window; the page is not in one here, so asking fails, which is a failure to report.
        await HeadlessUi.Run(async () =>
        {
            var notifications = new PrinterPageTabsTests.RecordingNotifications();
            var (page, commands, _) = PrinterPageTabsTests.Create(notifications: notifications);
            commands.Files.Add(new PrintMetadata("benchy.gcode", 1, "PLA", 0.4, 60));
            page.SelectedTabIndex = (int)PrinterPageTab.Files;

            await page.FileRows[0].DeleteCommand.ExecuteAsync(null);

            var error = Assert.Single(notifications.Errors);
            Assert.Equal("The file could not be deleted", error.Title);
        });
    }

    // A 1x1 PNG, the smallest picture there is.
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==");

    [Fact]
    public async Task The_thumbnail_shows_a_cube_until_the_picture_arrives_and_then_the_picture()
    {
        await HeadlessUi.Run(() =>
        {
            var (window, control, row, _, _) = Show();
            var icon = control.GetVisualDescendants().OfType<Avalonia.Controls.PathIcon>().First(i => i.Width == 24);
            var image = control.GetVisualDescendants().OfType<Image>().Single();
            Assert.True(icon.IsEffectivelyVisible);
            Assert.False(image.IsEffectivelyVisible);

            row.File.ThumbnailData = TinyPng;
            Dispatcher.UIThread.RunJobs();

            Assert.False(icon.IsEffectivelyVisible);
            Assert.True(image.IsEffectivelyVisible);
            window.Close();
        });
    }

    [Fact]
    public async Task The_picture_is_decoded_once_and_shared_by_the_image_and_its_preview()
    {
        await HeadlessUi.Run(() =>
        {
            var item = new PrintMetadataListItem(new PrintMetadata("a.gcode", 1, "PLA", 0.4, 60), TinyPng);
            var control = new PrinterFileRow
            {
                DataContext = new PrinterFileRowViewModel(item, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask, () => true),
            };
            var window = new Window { Content = control, Width = 900, Height = 200 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var image = control.GetVisualDescendants().OfType<Image>().Single();
            var preview = Assert.IsType<Image>(ToolTip.GetTip(image));

            Assert.NotNull(image.Source);
            Assert.Same(image.Source, preview.Source);
            window.Close();
        });
    }

    [Fact]
    public async Task The_file_dialog_item_uses_the_same_thumbnail()
    {
        await HeadlessUi.Run(() =>
        {
            var item = new PrintMetadataListItem(new PrintMetadata("a.gcode", 1, "PLA", 0.4, 60), TinyPng);
            var control = new PrintFileListItem { DataContext = item };
            var window = new Window { Content = control, Width = 600, Height = 200 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Single(control.GetVisualDescendants().OfType<FileThumbnail>());
            Assert.True(control.GetVisualDescendants().OfType<Image>().Single().IsEffectivelyVisible);
            window.Close();
        });
    }

    [Fact]
    public async Task The_row_shows_the_name_the_details_and_the_estimated_time()
    {
        await HeadlessUi.Run(() =>
        {
            var (window, control, _, _, _) = Show();

            var texts = control.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("benchy.gcode", texts);
            Assert.Contains("1h 30m", texts);
            Assert.Contains(texts, t => t?.StartsWith("PLA", StringComparison.Ordinal) == true);
            window.Close();
        });
    }
}
