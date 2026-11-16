using OctaGrid.Client.Contracts;

namespace OctaGrid.Client.Services;

/// <summary>An agent the user can manage, with the workspace it belongs to.</summary>
/// <param name="Id">The agent's id.</param>
/// <param name="Name">The agent's name.</param>
/// <param name="WorkspaceName">The name of the agent's workspace, to tell apart agents with the same name.</param>
public sealed record AgentChoice(Guid Id, string Name, string WorkspaceName)
{
    /// <summary>What a picker shows for the agent.</summary>
    public string DisplayName => $"{Name} · {WorkspaceName}";

    public override string ToString() => DisplayName;
}

/// <summary>
/// Lists the agents of every workspace the user belongs to. What runs on an agent (its extensions, for example) is
/// managed per agent, not per workspace, so the places that manage it pick from all of them.
/// </summary>
public sealed class AgentDirectory
{
    private readonly IAgentsClient _agents;
    private readonly IWorkspacesClient _workspaces;

    public AgentDirectory(IWorkspacesClient workspaces, IAgentsClient agents)
    {
        _workspaces = workspaces;
        _agents = agents;
    }

    /// <summary>The agents, workspace by workspace, in the order the backend lists them.</summary>
    public async Task<IReadOnlyList<AgentChoice>> GetAgentsAsync(CancellationToken cancellationToken)
    {
        var choices = new List<AgentChoice>();
        foreach (var workspace in await _workspaces.GetWorkspaceList(cancellationToken))
        {
            foreach (var agent in await _agents.GetAgentList(workspace.Id, cancellationToken))
            {
                choices.Add(new AgentChoice(agent.Id, agent.Name, workspace.Name));
            }
        }

        return choices;
    }
}
