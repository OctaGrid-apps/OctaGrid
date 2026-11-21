namespace OctaGrid.Data.Models;

/// <summary>
/// A user's membership of a workspace, part of the <see cref="Workspace"/> aggregate. Refers to the user by id: a
/// user is an aggregate of its own.
/// </summary>
public sealed class WorkspaceMember : Entity
{
    // For EF.
    private WorkspaceMember()
    {
    }

    public Guid WorkspaceId { get; private set; }
    public Guid UserId { get; private set; }
    public Role Role { get; private set; }
    public DateTimeOffset JoinedAt { get; private set; }

    public Workspace? Workspace { get; private set; }

    // The id is set here, not on save: several new members can be tracked at once only with ids of their own.
    internal static WorkspaceMember Create(Guid userId, Role role, DateTimeOffset joinedAt) => new()
    {
        Id = Guid.CreateVersion7(),
        UserId = userId,
        Role = role,
        JoinedAt = joinedAt,
    };
}
