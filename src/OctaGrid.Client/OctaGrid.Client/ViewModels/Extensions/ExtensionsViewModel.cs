using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Services;
using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Client.ViewModels;

/// <summary>
/// The extension management dialog: shows the extensions installed on one agent and turns them on or off. With more
/// than one agent the user picks which one.
/// </summary>
public partial class ExtensionsViewModel : ViewModelBase, IDialogParticipant<bool>
{
    private readonly AgentDirectory _agents;
    private readonly IExtensionsClient _client;
    private readonly ILogger<ExtensionsViewModel> _logger;
    private readonly INotificationService? _notifications;
    private readonly IAppSettingsStore? _settings;
    private bool _agentsLoaded;
    private bool _choosingAgent;

    public ExtensionsViewModel(
        IExtensionsClient client,
        AgentDirectory agents,
        INotificationService? notifications = null,
        ILogger<ExtensionsViewModel>? logger = null,
        IAppSettingsStore? settings = null)
    {
        _client = client;
        _agents = agents;
        _settings = settings;
        _notifications = notifications;
        _logger = logger ?? NullLogger<ExtensionsViewModel>.Instance;
    }

    public ObservableCollection<ExtensionItemViewModel> Items { get; } = [];

    public bool IsEmpty => Items.Count == 0;

    /// <summary>The agents whose extensions can be managed.</summary>
    public ObservableCollection<AgentChoice> Agents { get; } = [];

    /// <summary>The agent whose extensions are shown; null when there is none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAgent))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(CanManage))]
    public partial AgentChoice? SelectedAgent { get; set; }

    /// <summary>
    /// Whether the user may change the selected agent's extensions. Without it the extensions are only listed:
    /// installing, turning on or off, removing and developer mode are not shown.
    /// </summary>
    public bool CanManage => SelectedAgent?.CanManageExtensions ?? true;

    public bool HasAgent => SelectedAgent is not null;

    /// <summary>Whether to show the agent picker: with one agent there is nothing to choose.</summary>
    public bool HasSeveralAgents => Agents.Count > 1;

    /// <summary>True once the agents are loaded and there are none, so there is nowhere to install extensions.</summary>
    public bool HasNoAgent => _agentsLoaded && Agents.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    public partial bool IsBusy { get; set; }

    public bool CanEdit => !IsBusy && HasAgent;

    /// <summary>The folder extensions are loaded from in developer mode; null when developer mode is off.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDeveloperMode))]
    [NotifyPropertyChangedFor(nameof(DeveloperStatus))]
    public partial string? DeveloperFolder { get; set; }

    public bool IsDeveloperMode => DeveloperFolder is not null;

    public string DeveloperStatus => DeveloperFolder is null
        ? "Off. Point it at the folder your extension is built into, on the agent's computer: it loads without asking and reloads after every build."
        : $"On: {DeveloperFolder}";

    /// <summary>
    /// Turns developer mode on for a folder the user picked, on the selected agent. The choice is saved, with the agent,
    /// for the next start.
    /// </summary>
    public async Task SetDeveloperFolderAsync(string? folder)
    {
        await RunAsync("change developer mode for", async agentId =>
        {
            var result = await _client.SetDeveloperFolderAsync(agentId, folder, CancellationToken.None);
            if (result.Success)
            {
                var on = !string.IsNullOrWhiteSpace(folder);
                _settings?.Update(s => s with
                {
                    DeveloperExtensionsFolder = on ? folder : null,
                    DeveloperExtensionsAgentId = on ? agentId : null,
                });
            }

            Report(result, "Developer mode");
        });
    }

    [RelayCommand]
    private Task TurnOffDeveloperMode() => SetDeveloperFolderAsync(null);

    [RelayCommand]
    private async Task ReloadDeveloper()
    {
        await RunAsync("reload", async agentId =>
        {
            var result = await _client.ReloadDeveloperExtensionsAsync(agentId, CancellationToken.None);
            Report(result, "Developer mode");
        });
    }

    [RelayCommand]
    private async Task Load(CancellationToken cancellationToken)
    {
        if (!_agentsLoaded)
        {
            await LoadAgentsAsync(cancellationToken);
        }

        if (SelectedAgent is not { } agent)
        {
            Items.Clear();
            DeveloperFolder = null;
            OnPropertyChanged(nameof(IsEmpty));
            return;
        }

        var extensions = await _client.GetExtensionsAsync(agent.Id, cancellationToken);
        DeveloperFolder = await _client.GetDeveloperFolderAsync(agent.Id, cancellationToken);

        // Keep existing rows so the list does not flicker when it refreshes after a toggle.
        foreach (var info in extensions)
        {
            var existing = Items.FirstOrDefault(i => i.Id == info.Id);
            if (existing is null)
            {
                Items.Add(new ExtensionItemViewModel(info, OnToggled, OnReview, OnRemove, CanManage));
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

    /// <summary>Installs a package the user picked on the selected agent. It is not run until approved.</summary>
    /// <param name="openPackage">
    /// Opens the package (a <c>.octaext</c> or <c>.zip</c> file, or a folder the view zipped). It is called here, so a
    /// package that cannot be read is reported like any other failure.
    /// </param>
    public async Task InstallAsync(Func<CancellationToken, Task<Stream>> openPackage)
    {
        ArgumentNullException.ThrowIfNull(openPackage);

        await RunAsync("install", async agentId =>
        {
            ExtensionInstallResult result;
            try
            {
                await using var package = await openPackage(CancellationToken.None);
                result = await _client.InstallExtensionAsync(agentId, package, CancellationToken.None);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "The extension package could not be read");
                result = ExtensionInstallResult.Fail($"The package could not be read: {ex.Message}");
            }

            Report(result, "Extensions");
        });
    }

    private async Task LoadAgentsAsync(CancellationToken cancellationToken)
    {
        var agents = await _agents.GetAgentsAsync(cancellationToken);
        Agents.Clear();
        foreach (var agent in agents)
        {
            Agents.Add(agent);
        }

        _agentsLoaded = true;

        // Start on the agent developer mode was turned on for, where the user most likely works.
        var preferred = _settings?.ClientSettings.DeveloperExtensionsAgentId;
        _choosingAgent = true;
        try
        {
            SelectedAgent = Agents.FirstOrDefault(a => a.Id == preferred) ?? Agents.FirstOrDefault();
        }
        finally
        {
            _choosingAgent = false;
        }

        OnPropertyChanged(nameof(HasSeveralAgents));
        OnPropertyChanged(nameof(HasNoAgent));
    }

    partial void OnSelectedAgentChanged(AgentChoice? value)
    {
        if (!_choosingAgent)
        {
            _ = ShowAgentAsync();
        }
    }

    // Shows the extensions of the agent the user just picked.
    private async Task ShowAgentAsync()
    {
        IsBusy = true;
        try
        {
            await Load(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load the extensions of agent {AgentId}", SelectedAgent?.Id);
            _notifications?.ShowError("Extensions", "Could not read the extensions of this agent.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async void OnReview(ExtensionItemViewModel item)
    {
        var trust = new ExtensionTrustViewModel(item.Info);
        if (await this.ShowOverlayDialogAsync("Turn on this extension?", trust) != true)
        {
            return;
        }

        await RunAsync("approve", async agentId =>
        {
            var result = await _client.ApproveExtensionAsync(agentId, item.Id, CancellationToken.None);
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

        await RunAsync("remove", async agentId =>
        {
            var result = await _client.UninstallExtensionAsync(agentId, item.Id, CancellationToken.None);
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

    // Runs one action on the selected agent while the list is locked, then shows what the agent actually did.
    private async Task RunAsync(string action, Func<Guid, Task> work)
    {
        if (SelectedAgent is not { } agent)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await work(agent.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to {Action} an extension on agent {AgentId}", action, agent.Id);
            _notifications?.ShowError("Extensions", $"Could not {action} the extension.");
        }
        finally
        {
            await RefreshAsync();
            IsBusy = false;
        }
    }

    private async void OnToggled(ExtensionItemViewModel item, bool enabled)
    {
        if (SelectedAgent is not { } agent)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _client.SetExtensionEnabledAsync(agent.Id, item.Id, enabled, CancellationToken.None);
            _logger.LogInformation("Extension {ExtensionId} {State} on agent {AgentId} by user", item.Id, enabled ? "enabled" : "disabled", agent.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to {Action} extension {ExtensionId} on agent {AgentId}", enabled ? "enable" : "disable", item.Id, agent.Id);
            _notifications?.ShowError("Extensions", $"Could not {(enabled ? "enable" : "disable")} {item.Name}.");
        }
        finally
        {
            // Always show what the agent actually did, also after a failure.
            await RefreshAsync();
            IsBusy = false;
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            await Load(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh the extension list");
        }
    }
}
