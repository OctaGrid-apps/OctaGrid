using OctaGrid.Protocol.AgentLink.Replies;

namespace OctaGrid.Protocol.AgentLink.Requests;

/// <summary>Asks for what a printer wrote to its console, oldest first.</summary>
/// <param name="PrinterId">The printer.</param>
/// <param name="AfterSequence">The sequence of the last line already shown, or null for the newest lines.</param>
/// <param name="Limit">The most lines to return.</param>
public sealed record GetPrinterConsoleRequest(Guid PrinterId, long? AfterSequence, int Limit) : AgentRequest<PrinterConsoleReply>;
