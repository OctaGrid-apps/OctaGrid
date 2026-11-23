using System.Linq.Expressions;
using OctaGrid.Extensions.Models.Exceptions;

namespace OctaGrid.Data.Models;

/// <summary>
/// A person who uses OctaGrid, the root of the user aggregate. An identity provider signs the person in;
/// the backend keeps no password, only the <see cref="Logins"/> that map provider accounts to this user.
/// Other data refers to the user by <see cref="Entity.Id"/>, never by a provider's id.
/// </summary>
public sealed class User : Entity
{
    public const int MaxNameLength = 100;

    public const int MaxEmailLength = 256;

    private readonly List<ExternalLogin> _logins = [];

    // For EF.
    private User()
    {
    }

    /// <summary>The display name the identity provider last reported.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// The email address the identity provider last reported, or empty. Not unique: two providers
    /// may report the same address.
    /// </summary>
    public string Email { get; private set; } = string.Empty;

    /// <summary>The provider accounts that sign in as this user; only loaded ones are here.</summary>
    public IReadOnlyCollection<ExternalLogin> Logins => _logins;

    /// <summary>Creates a user. A name or email over its limit is cut.</summary>
    public static User Create(string name, string email) => new()
    {
        // Set here, not on save, so logins added before the save point to it.
        Id = Guid.CreateVersion7(),
        Name = Fit(name, MaxNameLength),
        Email = Fit(email, MaxEmailLength),
    };

    /// <summary>
    /// Matches the user that the account <paramref name="subject"/> at <paramref name="issuer"/> signs in as.
    /// </summary>
    public static Expression<Func<User, bool>> WithLogin(string issuer, string subject) =>
        u => u.Logins.Any(l => l.Issuer == issuer && l.Subject == subject);

    /// <summary>Adds the account <paramref name="subject"/> at <paramref name="issuer"/>.</summary>
    /// <exception cref="ValidationException">
    /// The issuer or subject is empty or too long, or the account is already a login of this user.
    /// </exception>
    public ExternalLogin AddLogin(string issuer, string subject)
    {
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        {
            throw new ValidationException(nameof(ExternalLogin), "A login needs an issuer and a subject.");
        }

        // Refused rather than cut: a cut id could map two accounts to one user.
        if (issuer.Length > ExternalLogin.MaxLength || subject.Length > ExternalLogin.MaxLength)
        {
            throw new ValidationException(nameof(ExternalLogin), "The issuer or subject of the login is too long.");
        }

        if (_logins.Any(l => l.Issuer == issuer && l.Subject == subject))
        {
            throw new ValidationException(nameof(ExternalLogin), "The account is already a login of this user.");
        }

        var login = ExternalLogin.Create(Id, issuer, subject);
        _logins.Add(login);
        return login;
    }

    /// <summary>Takes the name and email the provider reports. Returns whether either changed.</summary>
    public bool UpdateProfile(string name, string email)
    {
        name = Fit(name, MaxNameLength);
        email = Fit(email, MaxEmailLength);
        if (name == Name && email == Email)
        {
            return false;
        }

        Name = name;
        Email = email;
        return true;
    }

    private static string Fit(string? value, int maxLength)
    {
        value = value?.Trim() ?? string.Empty;
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
