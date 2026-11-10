using Avalonia.Controls;
using Avalonia.Interactivity;
using OctaGrid.Client.Services;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Views;

[ViewFor(typeof(PrinterPageViewModel))]
public partial class PrinterPageView : UserControl
{
    public PrinterPageView()
    {
        InitializeComponent();
    }

    private async void UploadFileButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PrinterPageViewModel page)
        {
            await FileUploadPicker.PickAndUploadAsync(this, page.Files);
        }
    }
}
