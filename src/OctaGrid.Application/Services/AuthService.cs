using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OctaGrid.Data.Abstractions;
using OctaGrid.Data.Abstractions.Models;
using OctaGrid.Protocol;
using OctaGrid.Protocol.Exceptions;

namespace OctaGrid.Application.Services;

public sealed class AuthService
{
    private readonly OctaGridContext _context;
    private readonly IPasswordHasher<User> _hasher;

    public AuthService(OctaGridContext context, IPasswordHasher<User> hasher)
    {
        _context = context;
        _hasher = hasher;
    }

    public async Task<UserResponse> ValidateUser(string userEmail, string password)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == userEmail)
            ?? throw new NotFoundException(nameof(User), userEmail);

        var verificationResult = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verificationResult == PasswordVerificationResult.Failed)
        {
            throw new AuthenticationException();
        }

        return new UserResponse(user.Id, user.Name, user.Email);

        //var claims = new[]
        //{
        //    new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
        //    new Claim(JwtRegisteredClaimNames.Email, user.Email),
        //    new Claim(ClaimTypes.Name, user.Name)
        //};

        //var token = Extensions.GenerateJwtToken(claims, _jwtOptions);

        //return new SignInResponse(token);
    }

    //public async Task<IActionResult> SwitchWorkspace([FromRoute] Guid workspaceId)
    //{
    //    var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
    //    if (!Guid.TryParse(userIdString, out var userId))
    //    {
    //        return Unauthorized();
    //    }

    //    var membership = await _context.WorkspaceMembers
    //        .FirstOrDefaultAsync(m => m.UserId == userId && m.WorkspaceId == workspaceId);

    //    if (membership is null)
    //    {
    //        return Forbid();
    //    }

    //    var newClaims = new[]
    //    {
    //        new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
    //        new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
    //        new Claim("workspace_id", workspaceId.ToString()),
    //        new Claim(ClaimTypes.Role, membership.Role.ToString(), typeof(Role).ToString())
    //    };

    //    var token = Extensions.GenerateJwtToken(newClaims, _jwtOptions);

    //    return Ok(new { token });
    //}
}
