using System.Diagnostics.CodeAnalysis;
using OctaGrid.Extensions.Abstractions;
using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Agent.Core.Connectors;

/// <summary>The connectors the agent knows about, found by connector id.</summary>
public sealed class ConnectorRegistry
{
    private readonly Dictionary<string, IConnector> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IConnector> _connectors = [];
    private readonly Dictionary<string, ExtensionSource> _sources = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>All registered connectors, in registration order.</summary>
    public IReadOnlyList<IConnector> Connectors => _connectors;

    /// <summary>Descriptors of all registered connectors, for clients.</summary>
    public IReadOnlyList<ConnectorDescriptor> Descriptors => _connectors.Select(c => c.Descriptor).ToList();

    /// <summary>Registers a connector under its id.</summary>
    /// <param name="connector">The connector.</param>
    /// <param name="source">Where the connector came from.</param>
    /// <exception cref="ArgumentException">The id is invalid or already registered.</exception>
    public ConnectorRegistry Add(IConnector connector, ExtensionSource source = ExtensionSource.Bundled)
    {
        ArgumentNullException.ThrowIfNull(connector);

        var id = connector.Descriptor.Id;
        if (!ExtensionId.IsValid(id))
        {
            throw new ArgumentException($"'{id}' is not a valid connector id.", nameof(connector));
        }

        if (!_byId.TryAdd(id, connector))
        {
            throw new ArgumentException($"A connector with id '{id}' is already registered.", nameof(connector));
        }

        _sources[id] = source;
        _connectors.Add(connector);
        return this;
    }

    /// <summary>Where a registered connector came from.</summary>
    public ExtensionSource GetSource(string id) => _sources[id];

    /// <summary>Finds a connector by id (case-insensitive).</summary>
    public bool TryGet(string? id, [NotNullWhen(true)] out IConnector? connector)
    {
        connector = null;
        return id is not null && _byId.TryGetValue(id, out connector);
    }
}
