using System.Text.Json.Serialization;

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
    public Guid? DefaultDashboard { get; init; }

    /// <summary>Folder extensions are loaded from in developer mode; null when developer mode is off.</summary>
    public string? DeveloperExtensionsFolder { get; init; }
}
