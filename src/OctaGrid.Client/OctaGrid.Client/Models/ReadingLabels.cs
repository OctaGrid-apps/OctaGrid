using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Models;

/// <summary>What the user sees for a reading key. Keys this client does not know are shown as they are.</summary>
public static class ReadingLabels
{
    /// <summary>The keys in the order the chart and its legend list them; other keys follow in the order they appear.</summary>
    public static IReadOnlyList<string> KnownOrder { get; } = [TelemetryReadingKeys.Nozzle, TelemetryReadingKeys.Bed];

    /// <summary>The label for a key.</summary>
    public static string For(string key) => key switch
    {
        TelemetryReadingKeys.Nozzle => "Hotend",
        TelemetryReadingKeys.Bed => "Bed",
        _ => key,
    };
}
