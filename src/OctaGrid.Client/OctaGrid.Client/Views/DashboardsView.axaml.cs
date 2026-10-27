using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Views;

[ViewFor(typeof(DashboardsViewModel))]
public partial class DashboardsView : ContentPage
{
    private readonly DashboardsViewModel _dataContext;

    public DashboardsView()
    {
        InitializeComponent();
        _dataContext = App.Services.GetRequiredService<DashboardsViewModel>();
        DataContext = _dataContext;

        Loaded += async (_, _) => await _dataContext.LoadDataCommand.ExecuteAsync(null);
    }
}
