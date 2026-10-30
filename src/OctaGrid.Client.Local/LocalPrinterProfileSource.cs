using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Agent.Core;
using OctaGrid.Application.Services;
using OctaGrid.Client.Contracts;
using OctaGrid.Protocol.Agents;

namespace OctaGrid.Client.Local;

internal sealed class LocalPrinterProfileSource(
    PrintersService printers,
    WorkspacesService workspaces,
    ICurrentUserAccessor currentUser,
    ILogger<LocalPrinterProfileSource>? logger = null)
    : IPrinterProfileSource
{
    private readonly ILogger<LocalPrinterProfileSource> _logger = logger ?? NullLogger<LocalPrinterProfileSource>.Instance;

    public async Task<IReadOnlyList<DeviceProfile>> GetProfilesAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.GetCurrentUserId()
            ?? throw new InvalidOperationException("No current user — call Me() first.");

        // Local mode is one workspace and agent per install — no agent-id filtering needed.
        var workspace = (await workspaces.GetList(userId, cancellationToken)).FirstOrDefault();
        if (workspace is null)
        {
            _logger.LogDebug("No workspace found for local user {UserId}; returning 0 device profiles", userId);
            return [];
        }

        var profiles = await printers.GetDeviceProfiles(userId, workspace.Id, cancellationToken);
        _logger.LogDebug("Loaded {ProfileCount} enabled printer profile(s) for workspace {WorkspaceId}", profiles.Count, workspace.Id);
        return profiles;
    }
}
