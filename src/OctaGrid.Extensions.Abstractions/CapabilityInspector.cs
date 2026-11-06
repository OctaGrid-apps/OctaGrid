using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Extensions.Abstractions;

/// <summary>
/// Works out which optional capabilities a connector implements, so the host can check that the
/// <see cref="ConnectorDescriptor.Capabilities"/> a connector declares match reality.
/// </summary>
public static class CapabilityInspector
{
    /// <summary>The capabilities implemented by a session (never includes <see cref="ConnectorCapabilities.Discovery"/>).</summary>
    public static ConnectorCapabilities Detect(IPrinterSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var found = ConnectorCapabilities.None;
        if (session is IPrintControl) found |= ConnectorCapabilities.PrintControl;
        if (session is IFileStorage) found |= ConnectorCapabilities.Files;
        if (session is IThumbnails) found |= ConnectorCapabilities.Thumbnails;
        if (session is ICustomCommands) found |= ConnectorCapabilities.CustomCommands;
        return found;
    }

    /// <summary>The capabilities implemented by the connector and a session it created.</summary>
    public static ConnectorCapabilities Detect(IConnector connector, IPrinterSession session)
    {
        ArgumentNullException.ThrowIfNull(connector);

        var found = Detect(session);
        if (connector is IPrinterDiscovery) found |= ConnectorCapabilities.Discovery;
        return found;
    }

    /// <summary>
    /// Compares the declared capabilities with the implemented ones.
    /// Returns null when they match, otherwise a message naming the differences.
    /// </summary>
    public static string? FindMismatch(IConnector connector, IPrinterSession session)
    {
        var declared = connector.Descriptor.Capabilities;
        var actual = Detect(connector, session);
        if (declared == actual)
        {
            return null;
        }

        var declaredOnly = declared & ~actual;
        var implementedOnly = actual & ~declared;
        var parts = new List<string>();
        if (declaredOnly != ConnectorCapabilities.None)
        {
            parts.Add($"declared but not implemented: {declaredOnly}");
        }

        if (implementedOnly != ConnectorCapabilities.None)
        {
            parts.Add($"implemented but not declared: {implementedOnly}");
        }

        return $"Connector '{connector.Descriptor.Id}' capabilities do not match ({string.Join("; ", parts)}).";
    }
}
