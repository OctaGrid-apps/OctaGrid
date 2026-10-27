namespace OctaGrid.Protocol.Workspaces;

public record WorkspaceSummaryResponse(Guid Id, string Name, string UserRole, int UsersCount, int AgentsCount, int PrintersCount);
