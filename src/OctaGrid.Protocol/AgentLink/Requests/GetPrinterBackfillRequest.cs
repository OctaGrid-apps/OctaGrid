using OctaGrid.Protocol.AgentLink.Replies;

namespace OctaGrid.Protocol.AgentLink.Requests;

/// <summary>Asks for the current state and recent history of printers a client starts showing.</summary>
/// <param name="Printers">The printers, each with how much of its history is wanted.</param>
public sealed record GetPrinterBackfillRequest(IReadOnlyList<PrinterBackfillQuery> Printers) : AgentRequest<PrinterBackfillReply>;

/// <summary>One printer in a <see cref="GetPrinterBackfillRequest"/>.</summary>
/// <param name="PrinterId">The printer.</param>
/// <param name="After">
/// The time of the last history sample the client already has, to get only what came after it; null for all of it.
/// </param>
public sealed record PrinterBackfillQuery(Guid PrinterId, DateTimeOffset? After);
