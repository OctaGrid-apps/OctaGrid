namespace OctaGrid.Data.Abstractions.Models;

public sealed class WorkspaceMember : Entity
{
    public Guid WorkspaceId { get; set; }
    public Guid UserId { get; set; }
    public Role Role { get; set; }
    public DateTimeOffset JoinedAt { get; set; }

    // Navigation props
    public Workspace? Workspace { get; set; }
    public User? User { get; set; }
}
