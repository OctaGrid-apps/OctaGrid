namespace OctaGrid.Data.Events;

/// <summary>A workspace was renamed.</summary>
/// <param name="WorkspaceId">The workspace.</param>
/// <param name="Name">Its new name.</param>
public sealed record WorkspaceRenamed(Guid WorkspaceId, string Name) : DomainEvent;
