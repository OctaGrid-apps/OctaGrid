using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Views;

[ViewFor(typeof(AgentListViewModel))]
public partial class AgentListView : ContentPage
{
    private readonly AgentListViewModel _dataContext;

    public AgentListView()
    {
        InitializeComponent();
        _dataContext = App.Services.GetRequiredService<AgentListViewModel>();
        DataContext = _dataContext;

        Loaded += async (_, _) => await _dataContext.LoadDataCommand.ExecuteAsync(null);
    }
}
