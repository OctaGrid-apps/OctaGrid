using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OctaGrid.Client.Dialogs;
using OctaGrid.Client.Services;
using OctaGrid.Client.ViewModels;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Client.Tests;

/// <summary>The file picker dialog as shown: its list and its selection.</summary>
[Collection(UiCollection.Name)]
public class FilePickerViewTests
{
    [Fact]
    public async Task Sorting_keeps_the_selected_file_selected()
    {
        var commands = new PrinterPageTabsTests.FakeCommands();
        commands.Files.AddRange([new PrintMetadata("a.3mf", 1, null, null, null), new PrintMetadata("b.3mf", 2, null, null, null)]);
        var picker = new FilePickerViewModel(Guid.NewGuid(), commands, new ByteArrayImageCache());
        await picker.LoadDataCommand.ExecuteAsync(null);

        await HeadlessUi.Run(() =>
        {
            var window = new Window { Content = new FilePickerView { DataContext = picker }, Width = 600, Height = 600 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var list = window.GetVisualDescendants().OfType<ListBox>().Single();
            var selected = picker.Files.Single(f => f.Metadata.FileName == "a.3mf");
            list.SelectedItem = selected;

            picker.SelectedSort = FileSortOption.All.Single(o => o.Order == FileSortOrder.LargestFirst);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(selected, picker.SelectedItem);
            Assert.Same(selected, list.SelectedItem);
            window.Close();
        });
    }
}
