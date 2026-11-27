using Microsoft.EntityFrameworkCore;
using OctaGrid.Application.Secrets;
using OctaGrid.Data;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Application.Agents;

/// <summary>
/// The enabled printers of an agent, with their settings and decrypted secrets, as pushed to the agent.
/// Backend-only: checks no user, and its result must never reach a client.
/// </summary>
public sealed class PrinterProfiles
{
    private readonly OctaGridContext _context;
    private readonly PrinterSecretStore _secrets;

    public PrinterProfiles(OctaGridContext context, PrinterSecretStore secrets)
    {
        _context = context;
        _secrets = secrets;
    }

    public async Task<IReadOnlyList<PrinterProfile>> ForAgent(Guid agentId, CancellationToken cancellationToken)
    {
        var printers = await _context.Printers
            .AsNoTracking()
            .Where(p => p.AgentId == agentId && p.Enabled)
            .Select(p => new { p.Id, p.ConnectorId, p.Settings, p.ProtectedSecrets })
            .ToListAsync(cancellationToken);

        // The connector receives plain settings and secrets together, by the keys it declared.
        return printers
            .Select(p =>
            {
                var settings = new Dictionary<string, string>(p.Settings);
                foreach (var (key, value) in _secrets.Read(p.Id, p.ProtectedSecrets).Values)
                {
                    settings[key] = value;
                }

                return new PrinterProfile(p.Id, p.ConnectorId, settings);
            })
            .ToList();
    }

    /// <summary>The agent's disabled printers: not run by the agent, but their data is kept.</summary>
    public async Task<IReadOnlyList<Guid>> DisabledForAgent(Guid agentId, CancellationToken cancellationToken) =>
        await _context.Printers
            .AsNoTracking()
            .Where(p => p.AgentId == agentId && !p.Enabled)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);
}
