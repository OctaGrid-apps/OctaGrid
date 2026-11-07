using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Services;
using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Client.ViewModels;

/// <summary>The extension management dialog: shows installed extensions and turns them on or off.</summary>
public partial class ExtensionsViewModel : ViewModelBase, IDialogParticipant<bool>
{
    private readonly IExtensionsClient _client;
    private readonly ILogger<ExtensionsViewModel> _logger;
    private readonly INotificationService? _notifications;
    private readonly IAppSettingsStore? _settings;

    public ExtensionsViewModel(
        IExtensionsClient client,
        INotificationService? notifications = null,
        ILogger<ExtensionsViewModel>? logger = null,
        IAppSettingsStore? settings = null)
    {
        _client = client;
        _settings = settings;
        _notifications = notifications;
        _logger = logger ?? NullLogger<ExtensionsViewModel>.Instance;
    }

    public ObservableCollection<ExtensionItemViewModel> Items { get; } = [];

    public bool IsEmpty => Items.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    public partial bool IsBusy { get; set; }

    public bool CanEdit => !IsBusy;

    /// <summary>The folder extensions are loaded from in developer mode; null when developer mode is off.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDeveloperMode))]
    [NotifyPropertyChangedFor(nameof(DeveloperStatus))]
    public partial string? DeveloperFolder { get; set; }

    public bool IsDeveloperMode => DeveloperFolder is not null;

    public string DeveloperStatus => DeveloperFolder is null
        ? "Off. Point it at the folder your extension is built into: it loads without asking and reloads after every build."
        : $"On: {DeveloperFolder}";

    /// <summary>Turns developer mode on for a folder the user picked. The choice is saved for the next start.</summary>
    public async Task SetDeveloperFolderAsync(string? folder)
    {
        await RunAsync("change developer mode for", async () =>
        {
            var result = await _client.SetDeveloperFolderAsync(folder, CancellationToken.None);
            if (result.Success)
            {
                _settings?.Update(s => s with { DeveloperExtensionsFolder = string.IsNullOrWhiteSpace(folder) ? null : folder });
            }

            Report(result, "Developer mode");
        });
    }

    [RelayCommand]
    private Task TurnOffDeveloperMode() => SetDeveloperFolderAsync(null);

    [RelayCommand]
    private async Task ReloadDeveloper()
    {
        await RunAsync("reload", async () =>
        {
            var result = await _client.ReloadDeveloperExtensionsAsync(CancellationToken.None);
            Report(result, "Developer mode");
        });
    }

    [RelayCommand]
    private async Task Load(CancellationToken cancellationToken)
    {
        var extensions = await _client.GetExtensionsAsync(cancellationToken);
        DeveloperFolder = await _client.GetDeveloperFolderAsync(cancellationToken);

        // Keep existing rows so the list does not flicker when it refreshes after a toggle.
        foreach (var info in extensions)
        {
            var existing = Items.FirstOrDefault(i => i.Id == info.Id);
            if (existing is null)
            {
                Items.Add(new ExtensionItemViewModel(info, OnToggled, OnReview, OnRemove));
            }
            else
            {
                existing.Update(info);
            }
        }

        foreach (var gone in Items.Where(i => extensions.All(e => e.Id != i.Id)).ToList())
        {
            Items.Remove(gone);
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private void Close() => this.ReturnResultFromOverlayDialog(true);

    /// <summary>Installs a package the user picked (a folder or a <c>.octaext</c> file). It is not run until approved.</summary>
    public async Task InstallAsync(string packagePath)
    {
        await RunAsync("install", async () =>
        {
            var result = await _client.InstallExtensionAsync(packagePath, CancellationToken.None);
            Report(result, "Extensions");
        });
    }

    private async void OnReview(ExtensionItemViewModel item)
    {
        var trust = new ExtensionTrustViewModel(item.Info);
        if (await this.ShowOverlayDialogAsync("Turn on this extension?", trust) != true)
        {
            return;
        }

        await RunAsync("approve", async () =>
        {
            var result = await _client.ApproveExtensionAsync(item.Id, CancellationToken.None);
            Report(result, item.Name);
        });
    }

    private async void OnRemove(ExtensionItemViewModel item)
    {
        var confirm = new ConfirmViewModel(
            $"Remove {item.Name}? Printers that use it stop working until you install it again.", "Remove");
        if (await this.ShowOverlayDialogAsync("Remove extension", confirm) != true)
        {
            return;
        }

        await RunAsync("remove", async () =>
        {
            var result = await _client.UninstallExtensionAsync(item.Id, CancellationToken.None);
            Report(result, item.Name);
        });
    }

    private void Report(ExtensionInstallResult result, string title)
    {
        if (result.Success)
        {
            _notifications?.ShowSuccess(title, result.Message);
        }
        else
        {
            _logger.LogWarning("Extension action failed: {Message}", result.Message);
            _notifications?.ShowError(title, result.Message);
        }
    }

    // Runs one action while the list is locked, then shows what the agent actually did.
    private async Task RunAsync(string action, Func<Task> work)
    {
        IsBusy = true;
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to {Action} an extension", action);
            _notifications?.ShowError("Extensions", $"Could not {action} the extension.");
        }
        finally
        {
            try
            {
                await Load(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh the extension list");
            }

            IsBusy = false;
        }
    }

    private async void OnToggled(ExtensionItemViewModel item, bool enabled)
    {
        IsBusy = true;
        try
        {
            await _client.SetExtensionEnabledAsync(item.Id, enabled, CancellationToken.None);
            _logger.LogInformation("Extension {ExtensionId} {State} by user", item.Id, enabled ? "enabled" : "disabled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to {Action} extension {ExtensionId}", enabled ? "enable" : "disable", item.Id);
            _notifications?.ShowError("Extensions", $"Could not {(enabled ? "enable" : "disable")} {item.Name}.");
        }
        finally
        {
            // Always show what the agent actually did, also after a failure.
            try
            {
                await Load(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh the extension list");
            }

            IsBusy = false;
        }
    }
}
