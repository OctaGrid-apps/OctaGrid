using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Extensions.Abstractions;

/// <summary>
/// The entry point of a connector extension. The host creates one instance per loaded extension.
/// A connector that can find printers on the network also implements <see cref="IPrinterDiscovery"/>.
/// </summary>
public interface IConnector
{
    /// <summary>Describes the connector, its settings and its capabilities.</summary>
    ConnectorDescriptor Descriptor { get; }

    /// <summary>Creates a session that talks to one printer.</summary>
    /// <param name="profile">The printer's saved settings.</param>
    /// <param name="context">Services provided by the host.</param>
    IPrinterSession CreateSession(PrinterProfile profile, IConnectorContext context);
}
