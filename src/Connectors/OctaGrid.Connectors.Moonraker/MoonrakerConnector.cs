using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Extensions.Abstractions;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Connectors.Moonraker;

/// <summary>Connector for Klipper printers through the Moonraker API (WebSocket JSON-RPC and HTTP).</summary>
public sealed class MoonrakerConnector(ILoggerFactory? loggerFactory = null) : IConnector
{
    public const string Id = "octagrid.moonraker";

    private readonly ILoggerFactory _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;

    public ConnectorDescriptor Descriptor { get; } = new(
        Id,
        "Moonraker (Klipper)",
        ConnectorVersion.Current(typeof(MoonrakerConnector)),
        "OctaGrid",
        ConnectorCapabilities.PrintControl | ConnectorCapabilities.Files | ConnectorCapabilities.Thumbnails | ConnectorCapabilities.Console
            | ConnectorCapabilities.ConsoleCommands,
        [
            new SettingField("url", "Printer address", SettingFieldType.Url,
                Help: "Host name or IP address of the Moonraker API, for example 192.168.1.20:7125."),
            new SettingField("apiKey", "API key", Required: false, Secret: true,
                Help: "Only needed when Moonraker requires authorization."),
        ],
        []);

    public IPrinterSession CreateSession(PrinterProfile profile, IConnectorContext context) =>
        new MoonrakerClient(profile, _loggerFactory);
}
