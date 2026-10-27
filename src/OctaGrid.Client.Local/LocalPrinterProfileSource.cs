using OctaGrid.Agent.Core;
using OctaGrid.Application.Services;
using OctaGrid.Client.Contracts;
using OctaGrid.Protocol.Agents;

namespace OctaGrid.Client.Local;

internal sealed class LocalPrinterProfileSource(PrintersService printers, WorkspacesService workspaces, ICurrentUserAccessor currentUser)
    : IPrinterProfileSource
{
    public async Task<IReadOnlyList<DeviceProfile>> GetProfilesAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.GetCurrentUserId()
            ?? throw new InvalidOperationException("No current user — call Me() first.");

        // Local mode is one workspace and agent per install — no agent-id filtering needed.
        var workspace = (await workspaces.GetList(userId, cancellationToken)).FirstOrDefault();

        return workspace is null ? [] : await printers.GetDeviceProfiles(userId, workspace.Id, cancellationToken);
    }
}
