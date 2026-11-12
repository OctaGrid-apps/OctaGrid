namespace OctaGrid.Data.Models;

public sealed class User : Entity
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;

    // Navigation props
    public ICollection<WorkspaceMember> WorkspaceMembers { get; set; } = [];
}
