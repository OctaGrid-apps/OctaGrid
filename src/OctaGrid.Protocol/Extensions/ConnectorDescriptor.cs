namespace OctaGrid.Protocol.Extensions;

/// <summary>
/// Plain-data description of a connector. It travels from the Agent to every client (including a
/// browser client) so the UI can list connectors, build the add-printer form and show commands
/// without any connector code running on the client.
/// </summary>
/// <param name="Id">Stable extension id in the form <c>publisher.name</c> (see <see cref="ExtensionId"/>).</param>
/// <param name="DisplayName">Name shown to users.</param>
/// <param name="Version">Version of the connector itself (SemVer).</param>
/// <param name="Publisher">Publisher shown to users.</param>
/// <param name="Capabilities">Optional features the connector supports.</param>
/// <param name="Settings">Settings the user provides for each printer.</param>
/// <param name="CustomCommands">Commands for Expert mode.</param>
/// <param name="IconUri">Optional icon (data URI or HTTPS URL).</param>
public sealed record ConnectorDescriptor(
    string Id,
    string DisplayName,
    string Version,
    string Publisher,
    ConnectorCapabilities Capabilities,
    IReadOnlyList<SettingField> Settings,
    IReadOnlyList<CustomCommandDescriptor> CustomCommands,
    string? IconUri = null);
