using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;

namespace OctaGrid.Client.ViewModels;

public partial class ModeSelectionViewModel : ViewModelBase
{
    private readonly IAppSettingsStore _appSettingsStore;

    public ModeSelectionViewModel(IAppSettingsStore appSettingsStore)
    {
        _appSettingsStore = appSettingsStore;
        AskOnNextLaunch = _appSettingsStore.ClientSettings.AskToSelectClientMode;
    }

    [ObservableProperty]
    public partial bool AskOnNextLaunch { get; set; }

    [RelayCommand]
    private async Task ChooseLocalMode(CancellationToken cancellationToken)
    {
        ChooseMode(ClientMode.Local);
        if (App.Current?.ApplicationLifetime is null)
        {
            throw new InvalidOperationException();
        }
        App.Current.ApplicationLifetime.Restart();
    }

    private void ChooseMode(ClientMode clientMode)
    {
        _appSettingsStore.Update(s => s with
        {
            Mode = clientMode,
            AskToSelectClientMode = AskOnNextLaunch
        });
    }

    [RelayCommand]
    private async Task ChooseRemoteMode(CancellationToken cancellationToken)
    {
        ChooseMode(ClientMode.Remote);
        if (App.Current?.ApplicationLifetime is null)
        {
            throw new InvalidOperationException();
        }
        App.Current.ApplicationLifetime.Restart();
    }
}
