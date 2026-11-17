using OctaGrid.Protocol.Permissions;

namespace OctaGrid.Protocol.Workspaces;

/// <summary>A workspace with its counts, as seen by the user who asked for it.</summary>
/// <param name="Id">The workspace.</param>
/// <param name="Name">Its name.</param>
/// <param name="UserRole">The name of the asking user's role, for display only. Decide what to show from <paramref name="Permissions"/>.</param>
/// <param name="UsersCount">How many members the workspace has.</param>
/// <param name="AgentsCount">How many agents the workspace has.</param>
/// <param name="PrintersCount">How many printers the workspace has.</param>
/// <param name="Permissions">
/// What the asking user may do in this workspace. Null when the backend did not say; the backend refuses what
/// is not allowed either way, so a client only uses this to decide what to show.
/// </param>
public record WorkspaceSummaryResponse(
    Guid Id,
    string Name,
    string UserRole,
    int UsersCount,
    int AgentsCount,
    int PrintersCount,
    IReadOnlyList<Permission>? Permissions = null);
