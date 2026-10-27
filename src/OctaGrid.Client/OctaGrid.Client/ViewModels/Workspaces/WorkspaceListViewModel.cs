using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;
using OctaGrid.Protocol.Workspaces;

namespace OctaGrid.Client.ViewModels;

public partial class WorkspaceListViewModel : ViewModelBase, IDialogParticipant
{
    private readonly ApplicationState _appState;
    private readonly IWorkspacesClient _workspacesClient;

    // Design-time only — see MainViewModel's parameterless constructor for why this exists.
    public WorkspaceListViewModel() : this(
        App.Services.GetRequiredService<IWorkspacesClient>(),
        App.Services.GetRequiredService<ApplicationState>())
    {
    }

    public WorkspaceListViewModel(IWorkspacesClient workspacesClient, ApplicationState appState)
    {
        _workspacesClient = workspacesClient;
        _appState = appState;
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

        _ = await _workspacesClient.CreateWorkspace(result.Name, cancellationToken);
        await LoadData(cancellationToken);
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

        _ = await _workspacesClient.UpdateWorkspace(parameter.Id, result.Name, cancellationToken);
        await LoadData(cancellationToken);
    }

    [RelayCommand]
    private async Task LoadData(CancellationToken cancellationToken)
    {
        var response = await _workspacesClient.GetWorkspacesSummary(cancellationToken);
        Workspaces = new ObservableCollection<WorkspaceSummaryResponse>(response);

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
}
