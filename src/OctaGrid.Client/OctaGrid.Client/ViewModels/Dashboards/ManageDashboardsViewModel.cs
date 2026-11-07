using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Services;
using OctaGrid.Protocol.Dashboards;
using OctaGrid.Protocol.Workspaces;

namespace OctaGrid.Client.ViewModels;

/// <summary>
/// The dashboard management dialog: lists the dashboards of a workspace and creates, renames, deletes them and picks
/// the default one. The result is true when anything changed, so the page knows to reload.
/// </summary>
public partial class ManageDashboardsViewModel : ViewModelBase, IDialogParticipant<bool>
{
    private readonly IDashboardsClient _client;
    private readonly ILogger<ManageDashboardsViewModel> _logger;
    private readonly INotificationService? _notifications;
    private readonly Guid _workspaceId;
    private bool _changed;

    public ManageDashboardsViewModel(
        IDashboardsClient client,
        Guid workspaceId,
        IEnumerable<DashboardResponse> dashboards,
        INotificationService? notifications = null,
        ILogger<ManageDashboardsViewModel>? logger = null)
    {
        _client = client;
        _workspaceId = workspaceId;
        _notifications = notifications;
        _logger = logger ?? NullLogger<ManageDashboardsViewModel>.Instance;
        Fill(dashboards);
    }

    public ObservableCollection<DashboardItemViewModel> Items { get; } = [];

    /// <summary>The kinds of dashboard that can be created. Add a kind here when its page exists.</summary>
    public IReadOnlyList<DashboardType> AvailableTypes { get; } = [DashboardType.List];

    [ObservableProperty]
    public partial DashboardType NewType { get; set; } = DashboardType.List;

    /// <summary>Only worth a picker once there is more than one kind to choose from.</summary>
    public bool CanChooseType => AvailableTypes.Count > 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    public partial bool IsBusy { get; set; }

    public bool CanEdit => !IsBusy;

    /// <summary>More than one saved dashboard is left, so any of them can be deleted.</summary>
    public bool CanDeleteAny => Items.Count(i => !i.IsNew) > 1;

    public bool HasNewItem => Items.Any(i => i.IsNew);

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        Items.Insert(0, new DashboardItemViewModel(this, null, NewType));
        OnPropertyChanged(nameof(HasNewItem));
        AddCommand.NotifyCanExecuteChanged();
    }

    private bool CanAdd() => !HasNewItem && !IsBusy;

    partial void OnIsBusyChanged(bool value) => AddCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private void Close() => this.ReturnResultFromOverlayDialog(_changed);

    internal void Discard(DashboardItemViewModel item)
    {
        Items.Remove(item);
        OnPropertyChanged(nameof(HasNewItem));
        AddCommand.NotifyCanExecuteChanged();
    }

    internal Task SaveAsync(DashboardItemViewModel item)
    {
        var name = item.EditName.Trim();
        return RunAsync(item.IsNew ? "create" : "rename", async () =>
        {
            if (item.IsNew)
            {
                await _client.CreateDashboard(_workspaceId, new CreateDashboardRequest(name, item.Type), CancellationToken.None);
            }
            else
            {
                await _client.UpdateDashboard(item.Id, new UpdateDashboardRequest(name), CancellationToken.None);
            }
        });
    }

    internal Task SetDefaultAsync(DashboardItemViewModel item) =>
        RunAsync("set as default", () => _client.SetDefaultDashboard(item.Id, CancellationToken.None));

    internal async Task DeleteAsync(DashboardItemViewModel item)
    {
        var confirm = new ConfirmViewModel(
            $"Delete \"{item.Name}\"? The dashboard and its layout are removed. Your printers stay in the workspace. This cannot be undone.",
            "Delete");
        if (await this.ShowOverlayDialogAsync("Delete dashboard", confirm) != true)
        {
            return;
        }

        await RunAsync("delete", () => _client.DeleteDashboard(item.Id, CancellationToken.None));
    }

    // Runs one change while the list is locked, then shows what is really stored, also after a failure.
    private async Task RunAsync(string action, Func<Task> work)
    {
        IsBusy = true;
        try
        {
            await work();
            _changed = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to {Action} a dashboard in workspace {WorkspaceId}", action, _workspaceId);
            _notifications?.ShowError("Dashboards", $"Could not {action} the dashboard. {ex.Message}");
        }
        finally
        {
            try
            {
                Fill(await _client.GetDashboardList(_workspaceId, CancellationToken.None));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to refresh the dashboard list");
            }

            IsBusy = false;
        }
    }

    private void Fill(IEnumerable<DashboardResponse> dashboards)
    {
        Items.Clear();
        foreach (var dashboard in dashboards)
        {
            Items.Add(new DashboardItemViewModel(this, dashboard));
        }

        OnPropertyChanged(nameof(CanDeleteAny));
        OnPropertyChanged(nameof(HasNewItem));
        AddCommand.NotifyCanExecuteChanged();
        foreach (var item in Items)
        {
            item.Refresh();
        }
    }
}
