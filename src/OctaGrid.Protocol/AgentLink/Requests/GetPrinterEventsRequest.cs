using OctaGrid.Protocol.AgentLink.Replies;

namespace OctaGrid.Protocol.AgentLink.Requests;

/// <summary>Asks for what happened to a printer, oldest first.</summary>
/// <param name="PrinterId">The printer.</param>
/// <param name="AfterSequence">The sequence of the last event already shown, or null for the newest events.</param>
/// <param name="Limit">The most events to return.</param>
public sealed record GetPrinterEventsRequest(Guid PrinterId, long? AfterSequence, int Limit) : AgentRequest<PrinterEventsReply>;
