using Avalonia.Controls;
using Avalonia.Platform.Storage;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Services;

/// <summary>Lets the user pick a file from the computer and uploads it to a printer. A view concern: it needs the window's storage provider.</summary>
internal static class FileUploadPicker
{
    public static async Task PickAndUploadAsync(Control owner, FilePickerViewModel files)
    {
        var topLevel = TopLevel.GetTopLevel(owner);
        if (topLevel is null)
        {
            return;
        }

        var picked = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a file to upload",
            AllowMultiple = false
        });

        if (picked.Count >= 1)
        {
            await using var stream = await picked[0].OpenReadAsync();
            await files.UploadFileCommand.ExecuteAsync(new UploadFileRequest(stream, picked[0].Name));
        }
    }
}
