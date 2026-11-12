namespace OctaGrid.Protocol.AgentLink;

/// <summary>A request on its way to an agent.</summary>
/// <param name="AgentId">The agent that has to handle it.</param>
/// <param name="CorrelationId">Chosen by the sender; the reply carries the same value.</param>
/// <param name="Request">What is asked.</param>
public sealed record AgentRequestEnvelope(Guid AgentId, Guid CorrelationId, AgentRequest Request);
