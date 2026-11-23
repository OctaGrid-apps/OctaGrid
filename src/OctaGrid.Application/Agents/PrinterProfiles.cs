using Microsoft.EntityFrameworkCore;
using OctaGrid.Application.Secrets;
using OctaGrid.Data;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Application.Agents;

/// <summary>
/// The printers an agent has to connect to (its enabled ones), with their settings and decrypted secrets. This is
/// what the backend pushes to the agent, by itself and for no user, so it is kept apart from the services a client
/// calls: its result must never reach a client.
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
}
