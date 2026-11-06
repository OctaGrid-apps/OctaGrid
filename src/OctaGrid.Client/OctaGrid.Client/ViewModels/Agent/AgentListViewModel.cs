using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;
using OctaGrid.Protocol.Agents;

namespace OctaGrid.Client.ViewModels;

public partial class AgentListViewModel : ViewModelBase, IDialogParticipant
{
    private readonly IAgentsClient _agentsClient;
    private readonly ApplicationState _appState;
    private readonly ILogger<AgentListViewModel> _logger;

    public AgentListViewModel(IAgentsClient agentsClient, ApplicationState appState, ILogger<AgentListViewModel>? logger = null)
    {
        _agentsClient = agentsClient;
        _appState = appState;
        _logger = logger ?? NullLogger<AgentListViewModel>.Instance;
    }

    [ObservableProperty]
    public partial ObservableCollection<AgentResponse> Agents { get; set; } = [];

    [RelayCommand]
    private async Task CreateAgent(CancellationToken cancellationToken)
    {
        var viewModel = new EditAgentViewModel(new AgentViewModel(new Models.Agent()));

        // Show the dialog and wait for the result.
        var result = await this.ShowOverlayDialogAsync("Create new agent", viewModel);
        if (result is null)
        {
            return;
        }

        var workspaceId = _appState.GetWorkspaceId();
        try
        {
            _logger.LogInformation("Creating agent '{AgentName}' in workspace {WorkspaceId}", result.Name, workspaceId);
            _ = await _agentsClient.CreateAgent(workspaceId, result.Name, cancellationToken);
            await LoadData(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to create agent '{AgentName}' in workspace {WorkspaceId}", result.Name, workspaceId);
            throw;
        }
    }

    [RelayCommand]
    private async Task EditAgent(AgentResponse agent, CancellationToken cancellationToken)
    {
        var viewModel = new EditAgentViewModel(new AgentViewModel(new Models.Agent()
        {
            Id = agent.Id,
            Name = agent.Name
        }));

        // Show the dialog and wait for the result.
        var result = await this.ShowOverlayDialogAsync("Edit agent", viewModel);
        if (result is null)
        {
            return;
        }

        try
        {
            _logger.LogInformation("Updating agent {AgentId} name to '{AgentName}'", agent.Id, result.Name);
            _ = await _agentsClient.UpdateAgent(agent.Id, result.Name, cancellationToken);
            await LoadData(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to update agent {AgentId}", agent.Id);
            throw;
        }
    }

    [RelayCommand]
    private async Task LoadData(CancellationToken cancellationToken)
    {
        if (_appState.SelectedWorkspace is null)
        {
            _logger.LogWarning("AgentListViewModel LoadData skipped: no workspace selected");
            return;
        }

        var workspaceId = _appState.GetWorkspaceId();
        try
        {
            var response = await _agentsClient.GetAgentList(workspaceId, cancellationToken);
            Agents = new ObservableCollection<AgentResponse>(response);
            _logger.LogDebug("Loaded {AgentCount} agent(s) for workspace {WorkspaceId}", response.Count, workspaceId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to load agents for workspace {WorkspaceId}", workspaceId);
            throw;
        }
    }
}
