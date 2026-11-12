using OctaGrid.Extensions.Models;
using OctaGrid.Protocol.AgentLink.Replies;

namespace OctaGrid.Protocol.AgentLink.Requests;

/// <summary>
/// Tells the agent which printers it runs. This is the complete set: the agent connects to the ones that are
/// new, reconnects the ones whose settings changed and drops the ones that are no longer listed.
/// The profiles carry the printers' secrets, so this message must never be logged.
/// </summary>
/// <param name="Profiles">Every printer the agent has to run.</param>
public sealed record ApplyPrinterProfilesRequest(IReadOnlyList<PrinterProfile> Profiles) : AgentRequest<AgentAck>;
