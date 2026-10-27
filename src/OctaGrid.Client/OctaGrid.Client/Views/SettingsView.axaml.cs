using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.Views;

public partial class SettingsView : ContentPage
{
    private readonly SettingsViewModel _dataContext;

    public SettingsView()
    {
        InitializeComponent();
        _dataContext = App.Services.GetRequiredService<SettingsViewModel>();
        DataContext = _dataContext;

        Loaded += async (_, _) => await _dataContext.LoadDataCommand.ExecuteAsync(null);
    }
}
