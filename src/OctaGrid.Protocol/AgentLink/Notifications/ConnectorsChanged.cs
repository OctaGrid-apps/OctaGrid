using OctaGrid.Extensions.Models;

namespace OctaGrid.Protocol.AgentLink.Notifications;

/// <summary>
/// Sent when the connectors an agent offers changed, for example after an extension was installed,
/// turned off or reloaded.
/// </summary>
/// <param name="Connectors">The complete new set.</param>
public sealed record ConnectorsChanged(IReadOnlyList<ConnectorDescriptor> Connectors) : AgentNotification;
