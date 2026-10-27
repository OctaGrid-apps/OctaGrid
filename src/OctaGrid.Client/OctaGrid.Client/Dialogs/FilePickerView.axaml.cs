using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Dialogs;

[ViewFor(typeof(FilePickerViewModel))]
public partial class FilePickerView : UserControl
{
    public FilePickerView()
    {
        InitializeComponent();
    }

    private async void UploadFileButton_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Text File",
            AllowMultiple = false
        });

        if (files.Count >= 1)
        {
            await using var stream = await files[0].OpenReadAsync();

            if (DataContext is FilePickerViewModel viewModel)
            {
                await viewModel.UploadFileCommand.ExecuteAsync(new UploadFileRequest(stream, files[0].Name));
            }
        }
    }
}
