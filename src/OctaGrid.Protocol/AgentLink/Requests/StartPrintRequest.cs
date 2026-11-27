using OctaGrid.Extensions.Models;
using OctaGrid.Protocol.AgentLink.Replies;

namespace OctaGrid.Protocol.AgentLink.Requests;

/// <summary>Starts printing a file that is on the printer.</summary>
/// <param name="PrinterId">The printer.</param>
/// <param name="FileName">The file to print, as the printer lists it.</param>
public sealed record StartPrintRequest(Guid PrinterId, string FileName) : AgentRequest<AgentAck>
{
    /// <summary>
    /// What the user chose from the file's start plan (<see cref="GetPrintStartPlanRequest"/>); null to start with
    /// the connector's defaults.
    /// </summary>
    public PrintStartChoices? Choices { get; init; }
}
