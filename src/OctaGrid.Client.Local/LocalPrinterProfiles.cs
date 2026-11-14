using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Application.Services;
using OctaGrid.Client.Contracts;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Client.Local;

/// <summary>
/// Works out which printers an agent has to run, with their settings and decrypted secrets.
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

    /// <summary>The enabled printers assigned to an agent, with their settings and decrypted secrets.</summary>
    public async Task<IReadOnlyList<PrinterProfile>> GetAsync(Guid agentId, CancellationToken cancellationToken)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UsersService>();
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

        var profiles = await printers.GetPrinterProfiles(userId, agentId, cancellationToken);
        _logger.LogDebug("Loaded {ProfileCount} enabled printer profile(s) for agent {AgentId}", profiles.Count, agentId);
        return profiles;
    }
}