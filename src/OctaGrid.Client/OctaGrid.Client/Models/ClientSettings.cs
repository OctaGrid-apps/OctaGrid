using System.Text.Json.Serialization;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Models;

public sealed record ClientSettings
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ClientMode Mode { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ElementTheme ElementTheme { get; init; } = ElementTheme.Default;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TransparencyLevel TransparencyLevel { get; init; } = TransparencyLevel.Mica;

    public string? ServerUrl { get; init; }
    public bool AskToSelectClientMode { get; init; }
    public bool IsSafetyDisclaimerAccepted { get; init; }
    public Guid? DefaultWorkspace { get; init; }

    /// <summary>Printer state the list dashboard is filtered by; null shows every state.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public UniversalPrinterState? DashboardStatusFilter { get; init; }

    /// <summary>Id of the connector the list dashboard is filtered by; null shows printers of every connector.</summary>
    public string? DashboardConnectorFilter { get; init; }

    /// <summary>Folder extensions are loaded from in developer mode; null when developer mode is off.</summary>
    public string? DeveloperExtensionsFolder { get; init; }
}
