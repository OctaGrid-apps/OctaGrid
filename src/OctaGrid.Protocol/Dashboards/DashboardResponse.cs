namespace OctaGrid.Protocol.Workspaces;

public record DashboardResponse(Guid Id, string Name, string Type, long Version);
