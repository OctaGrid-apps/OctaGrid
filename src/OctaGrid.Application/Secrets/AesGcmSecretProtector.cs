using System.Security.Cryptography;
using System.Text;

namespace OctaGrid.Application.Secrets;

/// <summary>
/// AES-256-GCM with a random nonce per value. Stored form, Base64 encoded:
/// format version (1 byte), nonce (12 bytes), tag (16 bytes), ciphertext.
/// </summary>
public sealed class AesGcmSecretProtector : ISecretProtector
{
    /// <summary>The key size in bytes.</summary>
    public const int KeySize = 32;

    private const byte FormatVersion = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = 1 + NonceSize + TagSize;

    private readonly ISecretKeySource _keySource;

    public AesGcmSecretProtector(ISecretKeySource keySource)
    {
        _keySource = keySource;
    }

    public string Protect(string plaintext, string purpose)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(purpose);

        var plain = Encoding.UTF8.GetBytes(plaintext);
        var output = new byte[HeaderSize + plain.Length];
        output[0] = FormatVersion;
        var nonce = output.AsSpan(1, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(Key(), TagSize);
        aes.Encrypt(nonce, plain, output.AsSpan(HeaderSize), output.AsSpan(1 + NonceSize, TagSize), Encoding.UTF8.GetBytes(purpose));
        return Convert.ToBase64String(output);
    }

    public string Unprotect(string protectedText, string purpose)
    {
        ArgumentNullException.ThrowIfNull(protectedText);
        ArgumentNullException.ThrowIfNull(purpose);

        byte[] input;
        try
        {
            input = Convert.FromBase64String(protectedText);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("The protected value is not valid.", ex);
        }

        if (input.Length < HeaderSize || input[0] != FormatVersion)
        {
            throw new CryptographicException("The protected value has an unknown format.");
        }

        var plain = new byte[input.Length - HeaderSize];
        using var aes = new AesGcm(Key(), TagSize);
        aes.Decrypt(input.AsSpan(1, NonceSize), input.AsSpan(HeaderSize), input.AsSpan(1 + NonceSize, TagSize), plain, Encoding.UTF8.GetBytes(purpose));
        return Encoding.UTF8.GetString(plain);
    }

    private byte[] Key()
    {
        var key = _keySource.GetOrCreateKey();
        if (key.Length != KeySize)
        {
            throw new CryptographicException($"The secret key must be {KeySize} bytes.");
        }

        return key;
    }
}
