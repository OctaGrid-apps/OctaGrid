using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Services;

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
                Items.Add(new ExtensionItemViewModel(info, OnToggled));
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
