namespace OctaGrid.Data.Events;

/// <summary>A printer was added to a workspace.</summary>
/// <param name="PrinterId">The printer.</param>
/// <param name="WorkspaceId">Its workspace.</param>
/// <param name="AgentId">The agent it was assigned to, if any.</param>
public sealed record PrinterAdded(Guid PrinterId, Guid WorkspaceId, Guid? AgentId) : DomainEvent;

/// <summary>A printer's name, connector, settings, secrets or enabled state changed.</summary>
/// <param name="PrinterId">The printer.</param>
/// <param name="WorkspaceId">Its workspace.</param>
public sealed record PrinterChanged(Guid PrinterId, Guid WorkspaceId) : DomainEvent;

/// <summary>A printer was assigned to another agent, or unassigned.</summary>
/// <param name="PrinterId">The printer.</param>
/// <param name="WorkspaceId">Its workspace.</param>
/// <param name="AgentId">The new agent, or null when the printer was unassigned.</param>
/// <param name="PreviousAgentId">The previous agent, or null when it had none.</param>
public sealed record PrinterAgentChanged(Guid PrinterId, Guid WorkspaceId, Guid? AgentId, Guid? PreviousAgentId) : DomainEvent;

/// <summary>A printer was removed.</summary>
/// <param name="PrinterId">The printer.</param>
/// <param name="WorkspaceId">Its workspace.</param>
/// <param name="AgentId">The agent it was assigned to, if any.</param>
public sealed record PrinterRemoved(Guid PrinterId, Guid WorkspaceId, Guid? AgentId) : DomainEvent;
