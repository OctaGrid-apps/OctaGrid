using OctaGrid.Protocol.Extensions;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Client.Models;

internal static class Extensions
{
    /// <summary>Builds a list entry, naming the connector and picking the address from its settings.</summary>
    public static PrinterListItem ToPrinterListItem(this PrinterResponse printer, IReadOnlyList<ConnectorDescriptor> connectors)
    {
        var connector = connectors.FirstOrDefault(c => string.Equals(c.Id, printer.ConnectorId, StringComparison.OrdinalIgnoreCase));
        return new PrinterListItem(
            printer.Id,
            printer.Name,
            printer.ConnectorId,
            connector?.DisplayName ?? printer.ConnectorId,
            PrinterDisplay.Address(connector, printer.Settings),
            printer.ConnectionState,
            printer.Settings,
            printer.Secrets,
            printer.Enabled,
            printer.Version,
            connector is not null);
    }
}

/// <summary>How a printer's settings are shown in lists.</summary>
public static class PrinterDisplay
{
    /// <summary>
    /// The value of the connector's first non-secret address setting; falls back to a setting
    /// called <c>url</c> when the connector is not available (for example when it is switched off).
    /// </summary>
    public static string Address(ConnectorDescriptor? connector, IReadOnlyDictionary<string, string> settings)
    {
        var key = connector?.Settings.FirstOrDefault(s => s.Type == SettingFieldType.Url && !s.Secret)?.Key ?? "url";
        return settings.TryGetValue(key, out var value) ? value : string.Empty;
    }
}
