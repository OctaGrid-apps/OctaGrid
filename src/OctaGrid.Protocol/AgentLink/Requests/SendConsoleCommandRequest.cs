using OctaGrid.Extensions.Models;
using OctaGrid.Protocol.AgentLink.Replies;

namespace OctaGrid.Protocol.AgentLink.Requests;

/// <summary>
/// Sends a command the user typed to a printer. It can change or stop what the printer is doing, so the
/// backend sends it only for a user who is allowed to, and the agent checks it against
/// <see cref="ConsoleCommandRules"/> again. What the printer answers appears in its console.
/// </summary>
/// <param name="PrinterId">The printer.</param>
/// <param name="Command">One line of text.</param>
public sealed record SendConsoleCommandRequest(Guid PrinterId, string Command) : AgentRequest<AgentAck>;
