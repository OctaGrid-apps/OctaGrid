using OctaGrid.Protocol.AgentLink.Replies;

namespace OctaGrid.Protocol.AgentLink.Requests;

/// <summary>Asks what starting a file involves: its plates and the connector's start options.</summary>
/// <param name="PrinterId">The printer.</param>
/// <param name="FileName">The file, as the printer lists it.</param>
public sealed record GetPrintStartPlanRequest(Guid PrinterId, string FileName) : AgentRequest<PrintStartPlanReply>;
