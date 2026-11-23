namespace OctaGrid.Application.Identity;

/// <summary>
/// The caller as an identity provider reports it. The host maps its provider's token claims to this,
/// so nothing past the host depends on the provider.
/// </summary>
/// <param name="Issuer">The provider: the token's <c>iss</c>.</param>
/// <param name="Subject">
/// The account's stable id at the provider. For Entra ID, <c>tid</c> and <c>oid</c>, not <c>sub</c>.
/// </param>
/// <param name="Name">The display name, or empty.</param>
/// <param name="Email">The email address, or empty.</param>
public sealed record ExternalIdentity(string Issuer, string Subject, string Name, string Email);
