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

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IAppSettingsStore _appSettings;
    private readonly ILogger<SettingsViewModel> _logger;

    public SettingsViewModel(IAppSettingsStore appSettingsStore, ILogger<SettingsViewModel>? logger = null)
    {
        _appSettings = appSettingsStore;
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
    private async Task OpenLogsFolder(CancellationToken cancellationToken)
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
}

public class Option<T>
{
    public Option(string displayText, T value)
    {
        DisplayText = displayText;
        Value = value;
    }

    public string DisplayText { get; set; } = "";
    public T Value { get; set; }
}
