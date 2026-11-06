using System.Diagnostics.CodeAnalysis;
using OctaGrid.Extensions.Abstractions;
using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Agent.Core.Connectors;

/// <summary>
/// Knows which extensions are installed, whether each is enabled, and remembers the user's choice.
/// Today every extension is a bundled connector; installed and Store extensions plug in here later.
/// </summary>
public sealed class ExtensionManager
{
    private readonly object _gate = new();
    private readonly HashSet<string> _disabled;
    private readonly ConnectorRegistry _registry;
    private readonly IExtensionStateStore _store;

    public ExtensionManager(ConnectorRegistry registry, IExtensionStateStore store)
    {
        _registry = registry;
        _store = store;
        _disabled = new HashSet<string>(store.LoadDisabled(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The installed connectors, for the orchestrator that creates sessions from them.</summary>
    public ConnectorRegistry Registry => _registry;

    /// <summary>Raised after an extension is enabled or disabled.</summary>
    public event Action? Changed;

    /// <summary>All installed extensions, ordered by name.</summary>
    public IReadOnlyList<ExtensionInfo> GetExtensions()
    {
        lock (_gate)
        {
            return _registry.Connectors
                .Select(ToInfo)
                .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    /// <summary>Descriptors of the connectors that are currently enabled.</summary>
    public IReadOnlyList<ConnectorDescriptor> EnabledDescriptors
    {
        get
        {
            lock (_gate)
            {
                return _registry.Connectors.Where(c => !_disabled.Contains(c.Descriptor.Id)).Select(c => c.Descriptor).ToList();
            }
        }
    }

    /// <summary>True when the user turned the extension off. Unknown ids are not disabled.</summary>
    public bool IsDisabled(string id)
    {
        lock (_gate)
        {
            return _disabled.Contains(id);
        }
    }

    /// <summary>Finds an installed connector, whether or not it is enabled.</summary>
    public bool TryGetConnector(string id, [NotNullWhen(true)] out IConnector? connector) =>
        _registry.TryGet(id, out connector);

    /// <summary>Turns an extension on or off and remembers the choice.</summary>
    /// <returns>False when no such extension is installed.</returns>
    public bool SetEnabled(string id, bool enabled)
    {
        if (!_registry.TryGet(id, out var connector))
        {
            return false;
        }

        var canonicalId = connector.Descriptor.Id;
        bool changed;
        lock (_gate)
        {
            changed = enabled ? _disabled.Remove(canonicalId) : _disabled.Add(canonicalId);
            if (changed)
            {
                _store.SaveDisabled(_disabled.ToList());
            }
        }

        if (changed)
        {
            Changed?.Invoke();
        }

        return true;
    }

    private ExtensionInfo ToInfo(IConnector connector)
    {
        var descriptor = connector.Descriptor;
        var source = _registry.GetSource(descriptor.Id);
        return new ExtensionInfo(
            descriptor.Id,
            descriptor.DisplayName,
            descriptor.Version,
            descriptor.Publisher,
            ExtensionKinds.Connector,
            source,
            _disabled.Contains(descriptor.Id) ? ExtensionState.Disabled : ExtensionState.Loaded,
            StateMessage: null,
            descriptor.Capabilities,
            CanUninstall: source != ExtensionSource.Bundled);
    }
}
