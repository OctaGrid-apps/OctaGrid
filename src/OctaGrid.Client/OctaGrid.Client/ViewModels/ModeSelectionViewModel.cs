using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;

namespace OctaGrid.Client.ViewModels;

public partial class ModeSelectionViewModel : ViewModelBase
{
    private readonly IAppSettingsStore _appSettingsStore;
    private readonly ILogger<ModeSelectionViewModel> _logger;

    public ModeSelectionViewModel(IAppSettingsStore appSettingsStore, ILogger<ModeSelectionViewModel>? logger = null)
    {
        _appSettingsStore = appSettingsStore;
        _logger = logger ?? NullLogger<ModeSelectionViewModel>.Instance;
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
        _logger.LogInformation("User selected client mode {Mode} (AskOnNextLaunch={AskOnNextLaunch})", clientMode, AskOnNextLaunch);
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
