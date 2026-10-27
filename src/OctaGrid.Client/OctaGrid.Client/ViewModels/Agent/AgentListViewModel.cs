using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;
using OctaGrid.Protocol.Agents;

namespace OctaGrid.Client.ViewModels;

public partial class AgentListViewModel : ViewModelBase, IDialogParticipant
{
    private readonly IAgentsClient _agentsClient;
    private readonly ApplicationState _appState;

    // Design-time only — see MainViewModel's parameterless constructor for why this exists.
    public AgentListViewModel() : this(
        App.Services.GetRequiredService<IAgentsClient>(),
        App.Services.GetRequiredService<ApplicationState>())
    {
    }

    public AgentListViewModel(IAgentsClient agentsClient, ApplicationState appState)
    {
        _agentsClient = agentsClient;
        _appState = appState;
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
        _ = await _agentsClient.CreateAgent(workspaceId, result.Name, cancellationToken);
        await LoadData(cancellationToken);
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
        _ = await _agentsClient.UpdateAgent(agent.Id, result.Name, cancellationToken);
        await LoadData(cancellationToken);
    }

    [RelayCommand]
    private async Task LoadData(CancellationToken cancellationToken)
    {
        if (_appState.SelectedWorkspace is null)
        {
            return;
        }

        var workspaceId = _appState.GetWorkspaceId();
        var response = await _agentsClient.GetAgentList(workspaceId, cancellationToken);
        Agents = new ObservableCollection<AgentResponse>(response);
    }
}
