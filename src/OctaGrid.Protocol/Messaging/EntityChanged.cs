namespace OctaGrid.Protocol.Messaging;

public sealed record EntityChanged(
    string EntityType,
    Guid EntityId,
    long Version,
    ChangeKind Kind,
    Guid WorkspaceId);
