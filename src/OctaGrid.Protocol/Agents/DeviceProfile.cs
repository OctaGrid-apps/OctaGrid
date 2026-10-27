namespace OctaGrid.Protocol.Agents;

public record DeviceProfile(Guid DeviceId, string Protocol, string ConnectionString, Dictionary<string, string> Settings);
