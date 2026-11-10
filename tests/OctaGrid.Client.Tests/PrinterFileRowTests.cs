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
