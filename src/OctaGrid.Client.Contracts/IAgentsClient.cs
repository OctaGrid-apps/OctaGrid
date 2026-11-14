using OctaGrid.Protocol.Agents;

namespace OctaGrid.Client.Contracts;

public interface IAgentsClient
{
    /// <summary>Makes an agent the one that connects to a printer, replacing the printer's previous agent.</summary>
    Task AssignPrinter(Guid agentId, Guid printerId, CancellationToken cancellationToken);

    Task<AgentResponse> CreateAgent(Guid workspaceId, string name, CancellationToken cancellationToken);

    Task<List<AgentResponse>> GetAgentList(Guid workspaceId, CancellationToken cancellationToken);

    Task<AgentResponse> UpdateAgent(Guid agentId, string name, CancellationToken cancellationToken);
}
