using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using OctaGrid.Client.Services;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Views;

[ViewFor(typeof(MainViewModel))]
public partial class MainView : UserControl
{
    private readonly MainViewModel _dataContext;

    public MainView()
    {
        InitializeComponent();
        _dataContext = App.Services.GetRequiredService<MainViewModel>();
        DataContext = _dataContext;
        Loaded += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is { } topLevel)
            {
                App.Services.GetRequiredService<INotificationService>().Attach(topLevel);
            }

            await _dataContext.LoadDataCommand.ExecuteAsync(null);
        };
    }
}
