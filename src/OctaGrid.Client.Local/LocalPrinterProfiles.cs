using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Application.Services;
using OctaGrid.Client.Contracts;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Client.Local;

/// <summary>
/// Works out which printers the local agent has to run, with their settings and decrypted secrets.
/// A singleton, so each read opens its own scope for the scoped services.
/// </summary>
internal sealed class LocalPrinterProfiles
{
    private readonly ICurrentUserAccessor _currentUser;
    private readonly ILogger<LocalPrinterProfiles> _logger;
    private readonly IServiceScopeFactory _scopes;

    public LocalPrinterProfiles(
        IServiceScopeFactory scopes, ICurrentUserAccessor currentUser, ILogger<LocalPrinterProfiles>? logger = null)
    {
        _scopes = scopes;
        _currentUser = currentUser;
        _logger = logger ?? NullLogger<LocalPrinterProfiles>.Instance;
    }

    public async Task<IReadOnlyList<PrinterProfile>> GetAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UsersService>();
        var workspaces = scope.ServiceProvider.GetRequiredService<WorkspacesService>();
        var printers = scope.ServiceProvider.GetRequiredService<PrintersService>();

        Guid userId;
        if (_currentUser.GetCurrentUserId() is { } known)
        {
            userId = known;
        }
        else
        {
            var user = await users.GetOrCreate(cancellationToken);
            _currentUser.SetCurrentUserId(user.Id);
            userId = user.Id;
        }

        // Local mode is one workspace and agent per install — no agent-id filtering needed.
        var workspace = (await workspaces.GetList(userId, cancellationToken)).FirstOrDefault();
        if (workspace is null)
        {
            _logger.LogDebug("No workspace found for local user {UserId}; returning 0 device profiles", userId);
            return [];
        }

        var profiles = await printers.GetPrinterProfiles(userId, workspace.Id, cancellationToken);
        _logger.LogDebug("Loaded {ProfileCount} enabled printer profile(s) for workspace {WorkspaceId}", profiles.Count, workspace.Id);
        return profiles;
    }
}
