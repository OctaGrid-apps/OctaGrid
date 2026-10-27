using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Views;

[ViewFor(typeof(WorkspaceListViewModel))]
public partial class WorkspaceListView : ContentPage
{
    private readonly WorkspaceListViewModel _dataContext;

    public WorkspaceListView()
    {
        InitializeComponent();
        _dataContext = App.Services.GetRequiredService<WorkspaceListViewModel>();
        DataContext = _dataContext;

        Loaded += async (_, _) => await _dataContext.LoadDataCommand.ExecuteAsync(null);
    }
}
