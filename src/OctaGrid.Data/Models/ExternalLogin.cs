namespace OctaGrid.Data.Models;

/// <summary>
/// An identity provider account that signs in as a <see cref="User"/>, part of the user aggregate.
/// <see cref="Issuer"/> and <see cref="Subject"/> together are unique.
/// </summary>
public sealed class ExternalLogin : Entity
{
    /// <summary>The longest <see cref="Issuer"/> or <see cref="Subject"/> accepted.</summary>
    public const int MaxLength = 256;

    // For EF.
    private ExternalLogin()
    {
    }

    public Guid UserId { get; private set; }

    /// <summary>The provider: the token's <c>iss</c>, or a fixed value for the Local mode user.</summary>
    public string Issuer { get; private set; } = string.Empty;

    /// <summary>The account's stable id at the provider (for Entra ID, the tenant and object ids).</summary>
    public string Subject { get; private set; } = string.Empty;

    // The id is set here, not on save: several new logins can be tracked at once only with ids of their own.
    internal static ExternalLogin Create(Guid userId, string issuer, string subject) => new()
    {
        Id = Guid.CreateVersion7(),
        UserId = userId,
        Issuer = issuer,
        Subject = subject,
    };
}
