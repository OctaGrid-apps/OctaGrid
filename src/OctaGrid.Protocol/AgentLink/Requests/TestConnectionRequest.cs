using OctaGrid.Protocol.AgentLink.Replies;

namespace OctaGrid.Protocol.AgentLink.Requests;

/// <summary>
/// Checks whether a printer can be reached with the given settings, before the printer is saved.
/// The settings include secrets, so this message must never be logged.
/// </summary>
/// <param name="ConnectorId">The connector to test with.</param>
/// <param name="Settings">The printer's settings and secrets, by the keys the connector declared.</param>
public sealed record TestConnectionRequest(string ConnectorId, IReadOnlyDictionary<string, string> Settings) : AgentRequest<ConnectionTestReply>;
