using OctaGrid.Protocol.Permissions;

namespace OctaGrid.Client.Models;

public record Workspace
{
    public Guid Id { get; set; } = Guid.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>What the user may do in this workspace, as the backend reported it. Null when it did not say.</summary>
    public IReadOnlyList<Permission>? Permissions { get; set; }
}
