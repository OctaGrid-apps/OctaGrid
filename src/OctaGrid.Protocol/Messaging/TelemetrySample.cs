using OctaGrid.Extensions.Models;

namespace OctaGrid.Protocol.Messaging;

/// <summary>
/// One point of a printer's telemetry history: every reading the agent knew at that moment. The agent keeps a bounded
/// history of these per printer so a client that opens the printer later can draw what it missed; it is kept apart from
/// <see cref="PrinterTelemetry"/>, which carries only the current state.
/// </summary>
/// <param name="Time">
/// When the agent stored the sample, on the agent's clock, so every client sees the same timeline. Samples of a printer
/// are stored in time order, and clients use the time as a cursor to ask for what came after the last sample they have.
/// </param>
/// <param name="Readings">The readings, one per sensor, each at most once.</param>
public sealed record TelemetrySample(DateTimeOffset Time, IReadOnlyList<TelemetryReading> Readings);

/// <summary>One sensor's value in a <see cref="TelemetrySample"/>.</summary>
/// <param name="Key">Which sensor; see <see cref="TelemetryReadingKeys"/>. Clients show keys they do not know as they are.</param>
/// <param name="Value">The measured value; for temperatures, degrees Celsius.</param>
/// <param name="Target">The value the printer is driving towards, or null when it has none or did not report it.</param>
public sealed record TelemetryReading(string Key, double Value, double? Target);

/// <summary>The keys of the readings the agent reports today.</summary>
public static class TelemetryReadingKeys
{
    /// <summary>The hotend temperature.</summary>
    public const string Nozzle = "nozzle";

    /// <summary>The bed temperature.</summary>
    public const string Bed = "bed";
}
