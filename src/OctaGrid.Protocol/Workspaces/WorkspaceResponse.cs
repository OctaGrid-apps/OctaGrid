using OctaGrid.Protocol.Permissions;

namespace OctaGrid.Protocol.Workspaces;

/// <summary>A workspace, as seen by the user who asked for it.</summary>
/// <param name="Id">The workspace.</param>
/// <param name="Name">Its name.</param>
/// <param name="Version">The version of the saved workspace, for optimistic concurrency.</param>
/// <param name="Permissions">
/// What the asking user may do in this workspace. Null when the backend did not say; the backend refuses what
/// is not allowed either way, so a client only uses this to decide what to show.
/// </param>
public record WorkspaceResponse(Guid Id, string Name, long Version, IReadOnlyList<Permission>? Permissions = null);
