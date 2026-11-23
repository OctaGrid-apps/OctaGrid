using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Data;
using OctaGrid.Data.Models;
using OctaGrid.Protocol;

namespace OctaGrid.Application.Identity;

/// <summary>
/// Finds the user a provider account signs in as, and creates it on the account's first sign-in.
/// The name and email follow what the provider reports.
/// </summary>
public sealed class UserProvisioning
{
    private readonly OctaGridContext _context;
    private readonly ILogger<UserProvisioning> _logger;

    public UserProvisioning(OctaGridContext context, ILogger<UserProvisioning>? logger = null)
    {
        _context = context;
        _logger = logger ?? NullLogger<UserProvisioning>.Instance;
    }

    /// <summary>The user the account signs in as, created on its first sign-in.</summary>
    public async Task<UserResponse> EnsureUserAsync(ExternalIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var user = await FindAsync(identity, cancellationToken);
        if (user is not null)
        {
            if (user.UpdateProfile(identity.Name, identity.Email))
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            return ToResponse(user);
        }

        user = User.Create(identity.Name, identity.Email);
        user.AddLogin(identity.Issuer, identity.Subject);
        _context.Users.Add(user);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent first sign-in of the same account created the user first.
            _context.ChangeTracker.Clear();
            var existing = await FindAsync(identity, cancellationToken);
            if (existing is null)
            {
                throw;
            }

            return ToResponse(existing);
        }

        _logger.LogInformation("Created user {UserId} for a first sign-in from {Issuer}", user.Id, identity.Issuer);
        return ToResponse(user);
    }

    private Task<User?> FindAsync(ExternalIdentity identity, CancellationToken cancellationToken) =>
        _context.Users
            .Where(User.WithLogin(identity.Issuer, identity.Subject))
            .FirstOrDefaultAsync(cancellationToken);

    private static UserResponse ToResponse(User user) => new(user.Id, user.Name, user.Email);
}
