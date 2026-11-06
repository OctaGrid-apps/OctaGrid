using Microsoft.Extensions.Logging;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Extensions.Abstractions;

/// <summary>Receives the state a session reports. Implemented by the host.</summary>
public interface ITelemetrySink
{
    /// <summary>Publishes the printer's current state. May be called from any thread.</summary>
    void Send(PrinterTelemetry telemetry);
}

/// <summary>Services the host gives to a connector session.</summary>
public interface IConnectorContext
{
    /// <summary>Logger scoped to the connector and printer.</summary>
    ILogger Logger { get; }

    /// <summary>A folder the connector may use for its own cache and files. Created on first use.</summary>
    string DataDirectory { get; }
}

/// <summary>A printer's saved configuration, handed to the connector when a session is created.</summary>
/// <param name="PrinterId">Id of the printer; use it for <see cref="PrinterTelemetry.PrinterId"/>.</param>
/// <param name="ConnectorId">Id of the connector that owns this printer.</param>
/// <param name="Settings">Values entered for the connector's declared settings, by key.</param>
public sealed record PrinterProfile(
    Guid PrinterId,
    string ConnectorId,
    IReadOnlyDictionary<string, string> Settings)
{
    /// <summary>Returns a setting's value, or null when it was not set.</summary>
    public string? Get(string key) => Settings.TryGetValue(key, out var value) ? value : null;
}
