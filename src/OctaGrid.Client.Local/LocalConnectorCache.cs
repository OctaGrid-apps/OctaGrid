using OctaGrid.Extensions.Models;

namespace OctaGrid.Client.Local;

/// <summary>
/// The connectors the local agent offers, kept so the UI can read them without waiting. The contract the UI
/// uses still exposes them as a plain property; the runtime fills this when it starts and the agent's
/// <c>ConnectorsChanged</c> notification keeps it current.
/// </summary>
internal sealed class LocalConnectorCache
{
    private volatile IReadOnlyList<ConnectorDescriptor> _connectors = [];

    /// <summary>The connectors as last reported by the agent. Empty until the runtime has started.</summary>
    public IReadOnlyList<ConnectorDescriptor> Connectors => _connectors;

    public void Set(IReadOnlyList<ConnectorDescriptor> connectors) => _connectors = connectors;
}
