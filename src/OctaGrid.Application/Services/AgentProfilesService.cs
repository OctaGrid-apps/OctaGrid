using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Application.Agents;
using OctaGrid.Protocol.AgentLink.Requests;

namespace OctaGrid.Application.Services;

/// <summary>
/// Gives an agent its printers. The agent never reads the database: the backend sends it the complete set when
/// it starts or connects, and again whenever a printer assigned to it changes. The host decides when; this is
/// the one place that knows what is sent.
/// </summary>
public sealed class AgentProfilesService
{
    private readonly IAgentLink _link;
    private readonly ILogger<AgentProfilesService> _logger;
    private readonly PrinterProfiles _profiles;

    public AgentProfilesService(PrinterProfiles profiles, IAgentLink link, ILogger<AgentProfilesService>? logger = null)
    {
        _profiles = profiles;
        _link = link;
        _logger = logger ?? NullLogger<AgentProfilesService>.Instance;
    }

    /// <summary>
    /// Sends the agent the printers assigned to it. Done by the backend itself, not on behalf of a user, so
    /// there is no user to authorize.
    /// </summary>
    public async Task Push(Guid agentId, CancellationToken cancellationToken)
    {
        var profiles = await _profiles.ForAgent(agentId, cancellationToken);
        var disabled = await _profiles.DisabledForAgent(agentId, cancellationToken);

        // Only the count: the profiles carry the printers' secrets.
        _logger.LogDebug("Sending {ProfileCount} printer profile(s) to agent {AgentId}", profiles.Count, agentId);
        await _link.SendAsync(
            agentId, new ApplyPrinterProfilesRequest(profiles) { DisabledPrinterIds = disabled }, cancellationToken);
    }
}
