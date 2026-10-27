using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Views;

[ViewFor(typeof(PrinterListViewModel))]
public partial class PrinterListView : ContentPage
{
    private readonly PrinterListViewModel _dataContext;

    public PrinterListView()
    {
        InitializeComponent();
        _dataContext = App.Services.GetRequiredService<PrinterListViewModel>();
        DataContext = _dataContext;
        Loaded += async (_, _) => await _dataContext.LoadDataCommand.ExecuteAsync(null);
    }
}
