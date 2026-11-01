using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;

namespace OctaGrid.Client.ViewModels;

public partial class SettingsViewModel : ViewModelBase, IDialogParticipant
{
    private readonly IAppSettingsStore _appSettings;
    private readonly IUpdateService _updateService;
    private readonly ILogger<SettingsViewModel> _logger;

    public SettingsViewModel(
        IAppSettingsStore appSettingsStore,
        IUpdateService? updateService = null,
        ILogger<SettingsViewModel>? logger = null)
    {
        _appSettings = appSettingsStore;
        _updateService = updateService ?? new DesignUpdateService();
        _logger = logger ?? NullLogger<SettingsViewModel>.Instance;
        AppClientModes.Add(new Option<ClientMode>(nameof(ClientMode.Local), ClientMode.Local));
        AppClientModes.Add(new Option<ClientMode>(nameof(ClientMode.Remote), ClientMode.Remote));

        AppElementThemes.Add(new Option<ElementTheme>(nameof(ElementTheme.Default), ElementTheme.Default));
        AppElementThemes.Add(new Option<ElementTheme>(nameof(ElementTheme.Dark), ElementTheme.Dark));
        AppElementThemes.Add(new Option<ElementTheme>(nameof(ElementTheme.Light), ElementTheme.Light));

        WindowTransparencyLevels.Add(new Option<TransparencyLevel>(nameof(TransparencyLevel.Mica), TransparencyLevel.Mica));
    }

    public ObservableCollection<Option<ClientMode>> AppClientModes { get; set; } = [];
    public ObservableCollection<Option<ElementTheme>> AppElementThemes { get; set; } = [];
    public string AppVersion { get; } = Helper.GetAppVersion();

    [ObservableProperty]
    public partial bool AskClientModeOnAppStart { get; set; }

    public string InfoVersion { get; } = Helper.GetInfoVersion();
    public bool IsClientModeApplied => IsAppClientModeApplied();
    public bool IsRemoteMode => _appSettings.StartupSettings.Mode == ClientMode.Remote;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClientModeApplied))]
    public partial Option<ClientMode>? SelectedAppClientMode { get; set; }

    [ObservableProperty]
    public partial Option<ElementTheme>? SelectedAppElementTheme { get; set; }

    [ObservableProperty]
    public partial Option<TransparencyLevel>? SelectedWindowTransparencyLevel { get; set; }

    public ObservableCollection<Option<TransparencyLevel>> WindowTransparencyLevels { get; set; } = [];

    [ObservableProperty]
    public partial UpdateStatus UpdateStatus { get; set; } = UpdateStatus.Idle;

    [ObservableProperty]
    public partial string UpdateStatusText { get; set; } = "Check for updates";

    [ObservableProperty]
    public partial int UpdateProgress { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCheckForUpdates))]
    public partial bool IsCheckingForUpdates { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyUpdate))]
    [NotifyPropertyChangedFor(nameof(CanCheckForUpdates))]
    public partial bool IsUpdating { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAvailableUpdate))]
    [NotifyPropertyChangedFor(nameof(CanApplyUpdate))]
    [NotifyPropertyChangedFor(nameof(UpdateActionText))]
    public partial AppUpdateInfo? AvailableUpdate { get; set; }

    public bool HasAvailableUpdate => AvailableUpdate is { IsUpdateAvailable: true };

    public bool CanCheckForUpdates => !IsCheckingForUpdates && !IsUpdating;

    public bool CanApplyUpdate => HasAvailableUpdate && !IsUpdating;

    public string UpdateChannelName => _updateService.Channel switch
    {
        UpdateChannel.MicrosoftStore => "Microsoft Store",
        UpdateChannel.Velopack => "Velopack",
        _ => "Manual"
    };

    public string UpdateActionText => _updateService.Channel == UpdateChannel.MicrosoftStore
        ? "Update"
        : (UpdateStatus == UpdateStatus.ReadyToRestart ? "Restart to apply" : "Update now");

    private bool IsAppClientModeApplied()
    {
        var cs = _appSettings.ClientSettings;
        var currentSelection = SelectedAppClientMode?.Value ?? cs.Mode;
        var startupMode = _appSettings.StartupSettings.Mode;
        return startupMode != ClientMode.Undefined && startupMode != currentSelection;
    }

    [RelayCommand]
    private async Task LoadData(CancellationToken cancellationToken)
    {
        var currentMode = _appSettings.ClientSettings.Mode != ClientMode.Undefined
            ? _appSettings.ClientSettings.Mode
            : _appSettings.StartupSettings.Mode != ClientMode.Undefined
                ? _appSettings.StartupSettings.Mode
                : ClientMode.Local;

        SelectedAppClientMode = AppClientModes.FirstOrDefault(x => x.Value == currentMode) ?? AppClientModes.First();
        SelectedAppElementTheme = AppElementThemes.FirstOrDefault(x => x.Value == _appSettings.ClientSettings.ElementTheme) ?? AppElementThemes.First();
        SelectedWindowTransparencyLevel = WindowTransparencyLevels.FirstOrDefault(x => x.Value == _appSettings.ClientSettings.TransparencyLevel) ?? WindowTransparencyLevels.First();
        AskClientModeOnAppStart = _appSettings.ClientSettings.AskToSelectClientMode;
    }

    [RelayCommand]
    private static async Task OpenLogsFolder(CancellationToken cancellationToken)
    {
        var folderPath = Helper.LogsDirectory;

        if (!Directory.Exists(folderPath))
        {
            return;
        }

        if (App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var topLevel = TopLevel.GetTopLevel(desktop.MainWindow);
            if (topLevel?.Launcher != null)
            {
                await topLevel.Launcher.LaunchUriAsync(new Uri(folderPath));
            }
        }
    }

    partial void OnAskClientModeOnAppStartChanged(bool value)
    {
        _logger.LogInformation("Setting AskToSelectClientMode changed to {Value}", value);
        _appSettings.Update(s => s with { AskToSelectClientMode = value });
    }

    partial void OnSelectedAppClientModeChanged(Option<ClientMode>? value)
    {
        if (value is null)
        {
            return;
        }

        _logger.LogInformation("Setting ClientMode changed to {Mode}", value.Value);
        _appSettings.Update(s => s with { Mode = value.Value });
    }

    partial void OnSelectedAppElementThemeChanged(Option<ElementTheme>? value)
    {
        if (value is null || _appSettings.ClientSettings.ElementTheme == value.Value)
        {
            return;
        }

        _logger.LogInformation("Setting ElementTheme changed to {Theme}", value.Value);
        _appSettings.Update(s => s with { ElementTheme = value.Value });

        Avalonia.Application.Current!.RequestedThemeVariant = value.Value switch
        {
            ElementTheme.Dark => ThemeVariant.Dark,
            ElementTheme.Light => ThemeVariant.Light,
            _ => ThemeVariant.Default
        };
    }

    partial void OnSelectedWindowTransparencyLevelChanged(Option<TransparencyLevel>? value)
    {
        if (value is null)
        {
            return;
        }

        _logger.LogInformation("Setting TransparencyLevel changed to {TransparencyLevel}", value.Value);
        _appSettings.Update(s => s with { TransparencyLevel = value.Value });
    }

    [RelayCommand]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static")]
    private void RestartApp()
    {
        if (App.Current?.ApplicationLifetime is { } lifetime)
        {
            lifetime.Restart();
        }
    }

    [RelayCommand]
    private async Task ShowSafetyDisclaimer()
    {
        _logger.LogInformation("Opening Safety Warning & Disclaimer dialog from Settings");
        var disclaimerViewModel = new SafetyDisclaimerViewModel();
        var accepted = await this.ShowOverlayDialogAsync("Safety Warning & Disclaimer", disclaimerViewModel);
        if (accepted && !_appSettings.ClientSettings.IsSafetyDisclaimerAccepted)
        {
            _appSettings.Update(s => s with { IsSafetyDisclaimerAccepted = true });
        }
    }

    [RelayCommand]
    private async Task CheckForUpdates(CancellationToken cancellationToken)
    {
        if (IsCheckingForUpdates || IsUpdating)
        {
            return;
        }

        IsCheckingForUpdates = true;
        UpdateStatus = UpdateStatus.Checking;
        UpdateStatusText = "Checking for updates...";

        try
        {
            var info = await _updateService.CheckForUpdatesAsync(cancellationToken);
            AvailableUpdate = info;

            if (info.IsUpdateAvailable)
            {
                UpdateStatus = UpdateStatus.UpdateAvailable;
                UpdateStatusText = $"Version {info.TargetVersion} is available";
            }
            else
            {
                UpdateStatus = UpdateStatus.UpToDate;
                UpdateStatusText = $"OctaGrid is up to date ({info.CurrentVersion})";
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to check for updates");
            UpdateStatus = UpdateStatus.Failed;
            UpdateStatusText = "Failed to check for updates";
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    [RelayCommand]
    private async Task ApplyUpdate(CancellationToken cancellationToken)
    {
        if (AvailableUpdate is not { IsUpdateAvailable: true } || IsUpdating)
        {
            return;
        }

        try
        {
            IsUpdating = true;
            UpdateStatus = UpdateStatus.Downloading;
            UpdateProgress = 0;
            UpdateStatusText = _updateService.Channel == UpdateChannel.MicrosoftStore
                ? "Launching Microsoft Store update..."
                : "Downloading update...";

            await _updateService.DownloadAndApplyUpdateAsync(AvailableUpdate, progress =>
            {
                UpdateProgress = progress;
                UpdateStatusText = progress < 100
                    ? $"Downloading update... {progress}%"
                    : "Installing and restarting...";
            }, cancellationToken);

            UpdateStatus = UpdateStatus.ReadyToRestart;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to download and apply update");
            UpdateStatus = UpdateStatus.Failed;
            UpdateStatusText = $"Update failed: {ex.Message}";
        }
        finally
        {
            IsUpdating = false;
        }
    }
}
