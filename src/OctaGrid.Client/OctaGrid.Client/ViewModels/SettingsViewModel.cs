using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;

namespace OctaGrid.Client.ViewModels;

public partial class SettingsViewModel : ViewModelBase, IDialogParticipant
{
    private readonly IAppSettingsStore _appSettings;
    private readonly IBackdropCapabilities _backdropCapabilities;
    private readonly ApplicationState _appState;
    private readonly IExtensionsClient? _extensionsClient;
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly INotificationService? _notifications;
    private readonly IUpdateService _updateService;

    public SettingsViewModel(
        IAppSettingsStore appSettingsStore,
        ApplicationState appState,
        IUpdateService? updateService = null,
        ILogger<SettingsViewModel>? logger = null,
        IExtensionsClient? extensionsClient = null,
        INotificationService? notifications = null,
        ILoggerFactory? loggerFactory = null,
        IBackdropCapabilities? backdropCapabilities = null)
    {
        _appSettings = appSettingsStore;
        _backdropCapabilities = backdropCapabilities ?? new NullBackdropCapabilities();
        _appState = appState;
        _updateService = updateService ?? new NullUpdateService();
        _extensionsClient = extensionsClient;
        _notifications = notifications;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = logger ?? NullLogger<SettingsViewModel>.Instance;

        _appState.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ApplicationState.AvailableUpdate))
            {
                ApplyUpdateInfo(_appState.AvailableUpdate);
            }
        };

        if (_appState.AvailableUpdate is not null)
        {
            ApplyUpdateInfo(_appState.AvailableUpdate);
        }

        AppClientModes.Add(new Option<ClientMode>(nameof(ClientMode.Local), ClientMode.Local));
        AppClientModes.Add(new Option<ClientMode>(nameof(ClientMode.Remote), ClientMode.Remote));

        AppElementThemes.Add(new Option<ElementTheme>(nameof(ElementTheme.Default), ElementTheme.Default));
        AppElementThemes.Add(new Option<ElementTheme>(nameof(ElementTheme.Dark), ElementTheme.Dark));
        AppElementThemes.Add(new Option<ElementTheme>(nameof(ElementTheme.Light), ElementTheme.Light));
    }

    public ObservableCollection<Option<ClientMode>> AppClientModes { get; set; } = [];

    public ObservableCollection<Option<ElementTheme>> AppElementThemes { get; set; } = [];

    public string AppVersion { get; } = Helper.GetAppVersion();

    [ObservableProperty]
    public partial bool AskClientModeOnAppStart { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAvailableUpdate))]
    [NotifyPropertyChangedFor(nameof(ShowApplyButton))]
    [NotifyPropertyChangedFor(nameof(ShowCheckButton))]
    [NotifyPropertyChangedFor(nameof(ReleaseNotesDescription))]
    [NotifyPropertyChangedFor(nameof(CanApplyUpdate))]
    [NotifyCanExecuteChangedFor(nameof(ApplyUpdateCommand))]
    [NotifyPropertyChangedFor(nameof(UpdateActionText))]
    [NotifyPropertyChangedFor(nameof(HasReleaseNotes))]
    [NotifyCanExecuteChangedFor(nameof(ShowReleaseNotesCommand))]
    public partial AppUpdateInfo? AvailableUpdate { get; set; }

    public bool CanApplyUpdate => HasAvailableUpdate && !IsUpdating;

    /// <summary>
    /// The download can be cancelled until it finishes; after that the update is installed and the app restarts.
    /// The Microsoft Store shows its own dialog, which this app cannot cancel.
    /// </summary>
    public bool CanCancelUpdate => IsUpdating && UpdateProgress < 100 && _updateService.Channel != UpdateChannel.MicrosoftStore;

    public bool CanCheckForUpdates => !IsCheckingForUpdates && !IsUpdating;

    /// <summary>The transparency toggle is only offered where the platform has a backdrop effect (Mica on Windows 11).</summary>
    public bool IsTransparencySupported => _backdropCapabilities.IsTransparencySupported;

    /// <summary>Whether this client can manage extensions at all.</summary>
    public bool CanManageExtensions => _extensionsClient is not null;

    [ObservableProperty]
    public partial string ExtensionsSummary { get; set; } = "Add support for more printers";

    public bool HasAvailableUpdate => AvailableUpdate is { IsUpdateAvailable: true };

    public bool HasReleaseNotes => HasAvailableUpdate && !string.IsNullOrWhiteSpace(AvailableUpdate?.ReleaseNotes);

    public string InfoVersion { get; } = Helper.GetInfoVersion();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCheckForUpdates))]
    [NotifyPropertyChangedFor(nameof(IsUpdateBusy))]
    [NotifyPropertyChangedFor(nameof(IsProgressIndeterminate))]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand))]
    public partial bool IsCheckingForUpdates { get; set; }

    public bool IsClientModeApplied => IsAppClientModeApplied();

    /// <summary>The progress is not known: while checking, and before the download reports its first percentage.</summary>
    public bool IsProgressIndeterminate => IsCheckingForUpdates || (IsUpdating && UpdateProgress <= 0);

    public bool IsRemoteMode => _appSettings.StartupSettings.Mode == ClientMode.Remote;

    /// <summary>A check or a download is under way, so the card shows a progress bar.</summary>
    public bool IsUpdateBusy => IsCheckingForUpdates || IsUpdating;

    /// <summary>Whether the Updates card is open. It opens by itself when an update starts, so its progress is visible.</summary>
    [ObservableProperty]
    public partial bool IsUpdatePanelExpanded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyUpdate))]
    [NotifyPropertyChangedFor(nameof(CanCheckForUpdates))]
    [NotifyPropertyChangedFor(nameof(CanCancelUpdate))]
    [NotifyPropertyChangedFor(nameof(ShowApplyButton))]
    [NotifyPropertyChangedFor(nameof(ShowCheckButton))]
    [NotifyPropertyChangedFor(nameof(IsUpdateBusy))]
    [NotifyPropertyChangedFor(nameof(IsProgressIndeterminate))]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyUpdateCommand))]
    public partial bool IsUpdating { get; set; }

    public string ReleaseNotesDescription => $"What's new in version {AvailableUpdate?.TargetVersion}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClientModeApplied))]
    public partial Option<ClientMode>? SelectedAppClientMode { get; set; }

    [ObservableProperty]
    public partial Option<ElementTheme>? SelectedAppElementTheme { get; set; }

    [ObservableProperty]
    public partial bool TransparencyEffects { get; set; } = true;

    // The collapsed Updates card has room for one button next to its title. Which one depends on what is going on, so
    // the buttons never crowd each other: Update (an update is waiting), Cancel (it is downloading), Check (nothing yet).
    public bool ShowApplyButton => HasAvailableUpdate && !IsUpdating;

    public bool ShowCheckButton => !HasAvailableUpdate && !IsUpdating;

    public string UpdateActionText => _updateService.Channel == UpdateChannel.MicrosoftStore
        ? "Update"
        : (UpdateStatus == UpdateStatus.ReadyToRestart ? "Restart to apply" : "Update and restart");

    public string UpdateChannelName => _updateService.Channel switch
    {
        UpdateChannel.MicrosoftStore => "Microsoft Store",
        UpdateChannel.Velopack => "Velopack",
        _ => "Manual"
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCancelUpdate))]
    [NotifyPropertyChangedFor(nameof(IsProgressIndeterminate))]
    public partial int UpdateProgress { get; set; }

    /// <summary>Explains the restart behavior next to the update button.</summary>
    public string UpdateRestartNotice => _updateService.Channel == UpdateChannel.MicrosoftStore
        ? "The update is installed by Microsoft Store."
        : "OctaGrid will close and restart automatically to finish the update. Save your work first.";

    [ObservableProperty]
    public partial UpdateStatus UpdateStatus { get; set; } = UpdateStatus.Idle;

    [ObservableProperty]
    public partial string UpdateStatusText { get; set; } = "Check for updates";

    /// <summary>
    /// Wraps the release notes in a page whose text follows the app theme. The page has no background of its own:
    /// the dialog's <see cref="NativeWebDialog.DefaultBackground"/> paints the window and the web view behind it.
    /// </summary>
    internal static string BuildReleaseNotesHtml(string notes, bool dark, string foreground, string link) =>
        "<!DOCTYPE html><html><head><meta charset=\"utf-8\">" +
        $"<meta name=\"color-scheme\" content=\"{(dark ? "dark" : "light")}\">" +
        "<style>" +
        $"body {{ color: {foreground}; font-family: 'Segoe UI', system-ui, sans-serif; margin: 16px; line-height: 1.5; }}" +
        $"a {{ color: {link}; }}" +
        "code, pre { background: color-mix(in srgb, currentColor 12%, transparent); border-radius: 4px; padding: 1px 4px; }" +
        "hr { border: 0; border-top: 1px solid color-mix(in srgb, currentColor 30%, transparent); }" +
        "</style></head><body>" + notes + "</body></html>";

    private static string Hex(Avalonia.Media.Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

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

    /// <summary>Reads a color from the theme resources for the current theme variant.</summary>
    private static Avalonia.Media.Color ThemeColor(string resourceKey, Avalonia.Media.Color fallback)
    {
        var app = Avalonia.Application.Current;
        return app is not null
            && app.TryGetResource(resourceKey, app.ActualThemeVariant, out var value)
            && value is Avalonia.Media.ISolidColorBrush brush
                ? brush.Color
                : fallback;
    }

    [RelayCommand(CanExecute = nameof(CanApplyUpdate), IncludeCancelCommand = true)]
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
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Update download cancelled by the user");
            UpdateProgress = 0;
            UpdateStatus = UpdateStatus.UpdateAvailable;
            UpdateStatusText = $"Update cancelled. Version {AvailableUpdate?.TargetVersion} is still available";
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

    private void ApplyUpdateInfo(AppUpdateInfo? info)
    {
        AvailableUpdate = info;
        if (info is { IsUpdateAvailable: true })
        {
            UpdateStatus = UpdateStatus.UpdateAvailable;
            UpdateStatusText = $"Version {info.TargetVersion} is available";
        }
        else if (info is not null)
        {
            UpdateStatus = UpdateStatus.UpToDate;
            UpdateStatusText = $"OctaGrid is up to date ({info.CurrentVersion})";
        }
    }

    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
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
            _appState.AvailableUpdate = info;
            ApplyUpdateInfo(info);
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
        TransparencyEffects = _appSettings.ClientSettings.TransparencyEffects;
        AskClientModeOnAppStart = _appSettings.ClientSettings.AskToSelectClientMode;
        await RefreshExtensionsSummaryAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task ManageExtensions()
    {
        if (_extensionsClient is null)
        {
            return;
        }

        _logger.LogInformation("Opening the extension management dialog from Settings");
        var viewModel = new ExtensionsViewModel(
            _extensionsClient,
            _notifications,
            _loggerFactory.CreateLogger<ExtensionsViewModel>(),
            _appSettings);
        await this.ShowOverlayDialogAsync("Extensions", viewModel);
        await RefreshExtensionsSummaryAsync(CancellationToken.None);
    }

    partial void OnAskClientModeOnAppStartChanged(bool value)
    {
        _logger.LogInformation("Setting AskToSelectClientMode changed to {Value}", value);
        _appSettings.Update(s => s with { AskToSelectClientMode = value });
    }

    partial void OnIsUpdatingChanged(bool value)
    {
        if (value)
        {
            IsUpdatePanelExpanded = true;
        }
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

    partial void OnTransparencyEffectsChanged(bool value)
    {
        if (_appSettings.ClientSettings.TransparencyEffects == value)
        {
            return;
        }

        _logger.LogInformation("Setting TransparencyEffects changed to {Value}", value);
        _appSettings.Update(s => s with { TransparencyEffects = value });
    }

    private async Task RefreshExtensionsSummaryAsync(CancellationToken cancellationToken)
    {
        if (_extensionsClient is null)
        {
            return;
        }

        try
        {
            var extensions = await _extensionsClient.GetExtensionsAsync(cancellationToken);
            var active = extensions.Count(e => e.State == Protocol.Extensions.ExtensionState.Loaded);
            ExtensionsSummary = extensions.Count == 0
                ? "No extensions installed"
                : $"{extensions.Count} installed, {active} active";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to read the installed extensions");
        }
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

    [RelayCommand(CanExecute = nameof(HasReleaseNotes))]
    private void ShowReleaseNotes()
    {
        var notes = AvailableUpdate?.ReleaseNotes;
        if (string.IsNullOrWhiteSpace(notes))
        {
            return;
        }

        try
        {
            var dark = Avalonia.Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
            var background = ThemeColor("OverlayDialogBackground", dark ? Avalonia.Media.Color.Parse("#171B25") : Avalonia.Media.Colors.White);
            var foreground = ThemeColor("SystemControlForegroundBaseHighBrush", dark ? Avalonia.Media.Colors.White : Avalonia.Media.Colors.Black);
            var link = ThemeColor("NavigationRailAccentBrush", foreground);
            var html = BuildReleaseNotesHtml(notes, dark, Hex(foreground), Hex(link));

            var dialog = new NativeWebDialog
            {
                Title = $"What's new in {AvailableUpdate?.TargetVersion}",
                CanUserResize = true,
                DefaultBackground = background,
            };
            dialog.NavigateToString(html);

            var owner = (App.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (owner is not null)
            {
                dialog.Show(owner);
            }
            else
            {
                dialog.Show();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show release notes");
        }
    }

    [RelayCommand]
    private async Task ShowSafetyDisclaimer()
    {
        _logger.LogInformation("Opening Safety Warning & Disclaimer dialog from Settings");
        var disclaimerViewModel = new SafetyDisclaimerViewModel(_loggerFactory.CreateLogger<SafetyDisclaimerViewModel>());
        var accepted = await this.ShowOverlayDialogAsync("Safety Warning & Disclaimer", disclaimerViewModel);
        if (accepted && !_appSettings.ClientSettings.IsSafetyDisclaimerAccepted)
        {
            _appSettings.Update(s => s with { IsSafetyDisclaimerAccepted = true });
        }
    }
}
