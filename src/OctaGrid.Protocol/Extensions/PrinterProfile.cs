namespace OctaGrid.Protocol.Extensions;

/// <summary>A printer's saved configuration, handed to the connector when a session is created.</summary>
/// <param name="PrinterId">Id of the printer; use it for <c>PrinterTelemetry.PrinterId</c>.</param>
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
