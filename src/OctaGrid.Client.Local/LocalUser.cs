using OctaGrid.Application.Identity;

namespace OctaGrid.Client.Local;

/// <summary>
/// The single user of Local mode, as a fixed provider account. Local mode has no identity provider,
/// so this account signs in the way a provider's account does in Remote mode.
/// </summary>
internal static class LocalUser
{
    /// <summary>An issuer no provider uses, so it never matches a real account.</summary>
    public const string Issuer = "urn:octagrid:local";

    public const string Subject = "local-user";

    public static ExternalIdentity Identity { get; } = new(Issuer, Subject, "User", string.Empty);
}
