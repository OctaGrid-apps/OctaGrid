namespace OctaGrid.Protocol.Workspaces;

public record DashboardResponse(Guid Id, string Name, DashboardType Type, bool IsDefault, long Version);
