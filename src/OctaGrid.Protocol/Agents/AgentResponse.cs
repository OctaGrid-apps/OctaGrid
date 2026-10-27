namespace OctaGrid.Protocol.Agents;

public record AgentResponse(Guid Id, string Name, string Status, string? Token, long Version);
