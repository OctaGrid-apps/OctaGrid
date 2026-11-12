using OctaGrid.Protocol.AgentLink.Replies;

namespace OctaGrid.Protocol.AgentLink.Requests;

/// <summary>Asks which connectors the agent can add printers with.</summary>
public sealed record GetConnectorsRequest : AgentRequest<ConnectorsReply>;
