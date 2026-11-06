namespace OctaGrid.Protocol.Extensions;

/// <summary>A printer found by a connector's network scan.</summary>
/// <param name="ConnectorId">The connector that can talk to this printer.</param>
/// <param name="Name">Name reported by the printer or derived from its address.</param>
/// <param name="Address">Host name or IP address.</param>
/// <param name="Model">Model, when the printer reports one.</param>
/// <param name="SuggestedSettings">Pre-filled values for the connector's settings form.</param>
public sealed record DiscoveredPrinter(
    string ConnectorId,
    string Name,
    string Address,
    string? Model,
    IReadOnlyDictionary<string, string> SuggestedSettings);
