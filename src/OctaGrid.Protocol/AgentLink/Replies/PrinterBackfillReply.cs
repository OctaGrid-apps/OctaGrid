using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Protocol.AgentLink.Replies;

/// <summary>The current state and recent history of the printers asked for, one entry per printer in the same order.</summary>
/// <param name="Printers">The printers.</param>
public sealed record PrinterBackfillReply(IReadOnlyList<PrinterBackfill> Printers) : AgentReply;
