namespace OctaGrid.Prototype;

/// <summary>Extension API version the host implements (MAJOR.MINOR).</summary>
public static class ExtensionApi
{
    public static readonly Version Current = new(1, 0);
}

public sealed record ConnectorDescriptor(string Id, string DisplayName, string Version);

/// <summary>Minimal stand-in for the IConnector contract drafted in ADR 0001.</summary>
public interface IConnector
{
    ConnectorDescriptor Descriptor { get; }

    string Ping();
}
