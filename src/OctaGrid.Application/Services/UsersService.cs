using Microsoft.EntityFrameworkCore;
using OctaGrid.Data;
using OctaGrid.Data.Models;
using OctaGrid.Extensions.Models.Exceptions;
using OctaGrid.Protocol;

namespace OctaGrid.Application.Services;

/// <summary>
/// Reads users. Users are created on their first sign-in by <see cref="Identity.UserProvisioning"/>.
/// </summary>
public sealed class UsersService
{
    private readonly OctaGridContext _context;

    public UsersService(OctaGridContext context)
    {
        _context = context;
    }

    public async Task<UserResponse> Get(Guid id, CancellationToken cancellationToken)
    {
        var response = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new UserResponse(u.Id, u.Name, u.Email))
            .FirstOrDefaultAsync(cancellationToken);

        return response ?? throw new NotFoundException(nameof(User), id);
    }
}
