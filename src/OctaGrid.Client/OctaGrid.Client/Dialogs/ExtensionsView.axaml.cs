using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Dialogs;

[ViewFor(typeof(ExtensionsViewModel))]
public partial class ExtensionsView : UserControl
{
    private static readonly FilePickerFileType ExtensionPackages = new("Extension packages")
    {
        Patterns = ["*.octaext", "*.zip"],
    };

    public ExtensionsView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DataContext is ExtensionsViewModel viewModel)
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
            }
        };
    }

    private async void OnInstallFromFile(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage || DataContext is not ExtensionsViewModel viewModel)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Install an extension",
            AllowMultiple = false,
            FileTypeFilter = [ExtensionPackages],
        });

        if (files.Count == 1 && files[0].TryGetLocalPath() is { } path)
        {
            await viewModel.InstallAsync(path);
        }
    }

    private async void OnInstallFromFolder(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage || DataContext is not ExtensionsViewModel viewModel)
        {
            return;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Install an extension from a folder",
            AllowMultiple = false,
        });

        if (folders.Count == 1 && folders[0].TryGetLocalPath() is { } path)
        {
            await viewModel.InstallAsync(path);
        }
    }
}
