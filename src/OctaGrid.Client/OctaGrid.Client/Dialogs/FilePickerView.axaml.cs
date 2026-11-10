using Avalonia.Controls;
using Avalonia.Interactivity;
using OctaGrid.Client.Services;
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
        if (DataContext is FilePickerViewModel viewModel)
        {
            await FileUploadPicker.PickAndUploadAsync(this, viewModel);
        }
    }
}
