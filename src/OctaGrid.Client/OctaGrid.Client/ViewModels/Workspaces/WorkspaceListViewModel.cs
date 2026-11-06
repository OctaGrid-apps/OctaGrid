using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;
using OctaGrid.Protocol.Workspaces;

namespace OctaGrid.Client.ViewModels;

public partial class WorkspaceListViewModel : ViewModelBase, IDialogParticipant
{
    private readonly ApplicationState _appState;
    private readonly ILogger<WorkspaceListViewModel> _logger;
    private readonly IWorkspacesClient _workspacesClient;

    public WorkspaceListViewModel(
        IWorkspacesClient workspacesClient,
        ApplicationState appState,
        ILogger<WorkspaceListViewModel>? logger = null)
    {
        _workspacesClient = workspacesClient;
        _appState = appState;
        _logger = logger ?? NullLogger<WorkspaceListViewModel>.Instance;
    }

    [ObservableProperty]
    public partial WorkspaceSummaryResponse? SelectedWorkspace { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<WorkspaceSummaryResponse> Workspaces { get; set; } = [];

    [RelayCommand]
    private async Task CreateWorkspace(CancellationToken cancellationToken)
    {
        var workspaceViewModel = new EditWorkspaceViewModel(new WorkspaceViewModel(new Workspace()));

        // Show the dialog and wait for the result.
        var result = await this.ShowOverlayDialogAsync("Create new workspace", workspaceViewModel);
        if (result is null)
        {
            return;
        }

        try
        {
            _logger.LogInformation("Creating workspace '{WorkspaceName}'", result.Name);
            _ = await _workspacesClient.CreateWorkspace(result.Name, cancellationToken);
            await LoadData(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to create workspace '{WorkspaceName}'", result.Name);
            throw;
        }
    }

    [RelayCommand]
    private async Task EditWorkspace(WorkspaceSummaryResponse parameter, CancellationToken cancellationToken)
    {
        var workspaceViewModel = new EditWorkspaceViewModel(new WorkspaceViewModel(new Workspace()
        {
            Id = parameter.Id,
            Name = parameter.Name
        }));

        var result = await this.ShowOverlayDialogAsync("Update workspace", workspaceViewModel);
        if (result is null)
        {
            return;
        }

        try
        {
            _logger.LogInformation("Updating workspace {WorkspaceId} name to '{WorkspaceName}'", parameter.Id, result.Name);
            _ = await _workspacesClient.UpdateWorkspace(parameter.Id, result.Name, cancellationToken);
            await LoadData(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to update workspace {WorkspaceId}", parameter.Id);
            throw;
        }
    }

    [RelayCommand]
    private async Task LoadData(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _workspacesClient.GetWorkspacesSummary(cancellationToken);
            Workspaces = new ObservableCollection<WorkspaceSummaryResponse>(response);
            _logger.LogDebug("Loaded {WorkspaceCount} workspace summary item(s)", response.Count);

            if (_appState.SelectedWorkspace is not null)
            {
                var workspace = Workspaces.FirstOrDefault(x => x.Id == _appState.SelectedWorkspace.Id);
                if (workspace is not null)
                {
                    SelectedWorkspace = workspace;
                }
            }
            else if (_appState.SelectedWorkspace is null && Workspaces.Count == 1)
            {
                var workspace = Workspaces.First();
                _appState.SelectedWorkspace = new Workspace()
                {
                    Id = workspace.Id,
                    Name = workspace.Name
                };
                SelectedWorkspace = workspace;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to load workspace summaries");
            throw;
        }
    }
}
