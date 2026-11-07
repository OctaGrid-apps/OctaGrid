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

    public ExtensionsViewModel(
        IExtensionsClient client,
        INotificationService? notifications = null,
        ILogger<ExtensionsViewModel>? logger = null)
    {
        _client = client;
        _notifications = notifications;
        _logger = logger ?? NullLogger<ExtensionsViewModel>.Instance;
    }

    public ObservableCollection<ExtensionItemViewModel> Items { get; } = [];

    public bool IsEmpty => Items.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    public partial bool IsBusy { get; set; }

    public bool CanEdit => !IsBusy;

    [RelayCommand]
    private async Task Load(CancellationToken cancellationToken)
    {
        var extensions = await _client.GetExtensionsAsync(cancellationToken);

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
