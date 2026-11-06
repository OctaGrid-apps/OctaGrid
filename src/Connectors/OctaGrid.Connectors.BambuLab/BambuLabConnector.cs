using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Extensions.Abstractions;
using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Connectors.BambuLab;

/// <summary>Connector for Bambu Lab printers in LAN mode (MQTT for state and control, FTPS for files).</summary>
public sealed class BambuLabConnector(ILoggerFactory? loggerFactory = null) : IConnector
{
    public const string Id = "octagrid.bambu-mqtt";

    private readonly ILoggerFactory _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;

    public ConnectorDescriptor Descriptor { get; } = new(
        Id,
        "Bambu Lab (LAN mode)",
        ConnectorVersion.Current(typeof(BambuLabConnector)),
        "OctaGrid",
        ConnectorCapabilities.PrintControl | ConnectorCapabilities.Files | ConnectorCapabilities.Thumbnails,
        [
            new SettingField("url", "Printer address", SettingFieldType.Url,
                Help: "IP address of the printer on your network."),
            new SettingField("apiKey", "Access code", Secret: true,
                Help: "The LAN access code shown on the printer."),
            new SettingField("serialNumber", "Serial number", Required: false,
                Help: "Optional. Detected automatically when left empty."),
        ],
        []);

    public IPrinterSession CreateSession(PrinterProfile profile, IConnectorContext context) =>
        new MqttClient(profile, _loggerFactory.CreateLogger<MqttClient>());
}
