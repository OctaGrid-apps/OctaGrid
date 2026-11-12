using OctaGrid.Extensions.Models;

namespace OctaGrid.Protocol.AgentLink.Replies;

/// <summary>The connectors an agent can add printers with.</summary>
/// <param name="Connectors">The connectors, with their settings and capabilities.</param>
public sealed record ConnectorsReply(IReadOnlyList<ConnectorDescriptor> Connectors) : AgentReply;
