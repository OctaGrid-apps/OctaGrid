using System.Text.Json.Serialization;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Client.Models;

public sealed record ClientSettings
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ClientMode Mode { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ElementTheme ElementTheme { get; init; } = ElementTheme.Default;

    /// <summary>Show the OS transparency effect (Mica) behind the window. Ignored where the platform has none.</summary>
    public bool TransparencyEffects { get; init; } = true;

    public string? ServerUrl { get; init; }
    public bool AskToSelectClientMode { get; init; }
    public bool IsSafetyDisclaimerAccepted { get; init; }
    public Guid? DefaultWorkspace { get; init; }

    /// <summary>Printer state the list dashboard is filtered by; null shows every state.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public UniversalPrinterState? DashboardStatusFilter { get; init; }

    /// <summary>Id of the connector the list dashboard is filtered by; null shows printers of every connector.</summary>
    public string? DashboardConnectorFilter { get; init; }

    /// <summary>
    /// Shows controls that can interrupt a print or change how a printer behaves, such as sending G-code commands. Off
    /// until the user turns it on in Settings, and it stays on across restarts.
    /// </summary>
    public bool IsExpertMode { get; init; }

    /// <summary>Folder extensions are loaded from in developer mode; null when developer mode is off.</summary>
    public string? DeveloperExtensionsFolder { get; init; }
}
