namespace OctaGrid.Application.Secrets;

/// <summary>
/// Encrypts secrets before they are written to the database and decrypts them when they are needed.
/// The key is not kept in the database, so the database file alone does not reveal them.
/// </summary>
public interface ISecretProtector
{
    /// <summary>Encrypts text. The result can be stored as text.</summary>
    /// <param name="plaintext">The text to protect.</param>
    /// <param name="purpose">
    /// What the value belongs to (for example a printer id). The same value must be passed to
    /// <see cref="Unprotect"/>, so protected text moved to another record cannot be read there.
    /// </param>
    string Protect(string plaintext, string purpose);

    /// <summary>Decrypts text produced by <see cref="Protect"/>.</summary>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// The text was changed, belongs to another purpose, or was protected with another key.
    /// </exception>
    string Unprotect(string protectedText, string purpose);
}
