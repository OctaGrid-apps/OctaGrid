using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Data.Abstractions;
using OctaGrid.Data.Abstractions.Models;
using OctaGrid.Protocol;
using OctaGrid.Protocol.Exceptions;
using OctaGrid.Protocol.Users;

namespace OctaGrid.Application.Services;

public sealed class UsersService
{
    private readonly OctaGridContext _context;
    private readonly IPasswordHasher<User> _hasher;
    private readonly ILogger<UsersService> _logger;

    public UsersService(OctaGridContext context, IPasswordHasher<User> hasher, ILogger<UsersService>? logger = null)
    {
        _context = context;
        _hasher = hasher;
        _logger = logger ?? NullLogger<UsersService>.Instance;
    }

    public async Task<UserResponse> Create(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var userExists = await _context.Users
            .AsNoTracking()
            .Where(u => u.Email == request.Email)
            .FirstOrDefaultAsync(cancellationToken);

        if (userExists is not null)
        {
            _logger.LogWarning("User registration rejected: email '{Email}' is already registered", request.Email);
            throw new ValidationException(nameof(request.Email), "User with this email already exists.");
        }

        var user = new User
        {
            Name = request.Name,
            Email = request.Email
        };

        user.PasswordHash = _hasher.HashPassword(user, request.Password);
        _context.Users.Add(user);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Registered new user {UserId} ('{Email}')", user.Id, user.Email);
        return new UserResponse(user.Id, user.Name, user.Email);
    }

    public async Task<UserResponse> Get(Guid id, CancellationToken cancellationToken)
    {
        var response = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new UserResponse(u.Id, u.Name, u.Email))
            .FirstOrDefaultAsync(cancellationToken);

        return response is null ? throw new NotFoundException(nameof(User), id) : response;
    }

    /// <summary>
    /// Method used for local mode only
    /// </summary>
    public async Task<UserResponse> GetOrCreate(CancellationToken cancellationToken)
    {
        var user = await _context.Users.FirstOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            user = new User()
            {
                Name = "User",
                Email = "User@example.com"
            };
            user.PasswordHash = _hasher.HashPassword(user, GeneratePassword());

            await _context.Users.AddAsync(user, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Auto-provisioned default local user {UserId}", user.Id);
        }

        return new UserResponse(user.Id, user.Name, user.Email);
    }

    public async Task<UserResponse> SignIn(SignInRequest request, CancellationToken cancellationToken)
    {
        var user = await _context.Users
            .Where(u => u.Email == request.Email)
            .FirstOrDefaultAsync(cancellationToken);

        // Deliberately the same error for "no such user" and "wrong password" — don't let a
        // sign-in attempt reveal which emails have accounts.
        if (user is null || _hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
        {
            _logger.LogWarning("Sign-in failed for email '{Email}'", request.Email);
            throw new AuthenticationException();
        }

        _logger.LogInformation("User {UserId} signed in successfully", user.Id);
        return new UserResponse(user.Id, user.Name, user.Email);
    }

    private static string GeneratePassword()
    {
        var randomGenerator = RandomNumberGenerator.Create();
        var data = new byte[8];
        randomGenerator.GetBytes(data);
        return BitConverter.ToString(data);
    }
}
