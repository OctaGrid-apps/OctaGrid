using System.Security.Cryptography;
using Meziantou.Framework.Win32;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Application.Secrets;

namespace OctaGrid.Client.Desktop.Services;

/// <summary>
/// Keeps the key that encrypts printer secrets in the Windows Credential Manager, which encrypts it for the
/// signed-in user. The database holds only what was encrypted with it, so a copy of the database file alone
/// does not reveal any printer secret.
/// </summary>
internal sealed class WindowsSecretKeySource : ISecretKeySource
{
    private const string CredentialName = "OctaGrid/secrets-key";
    private readonly object _lock = new();
    private readonly ILogger<WindowsSecretKeySource> _logger;
    private byte[]? _key;

    public WindowsSecretKeySource(ILogger<WindowsSecretKeySource>? logger = null)
    {
        _logger = logger ?? NullLogger<WindowsSecretKeySource>.Instance;
    }

    public byte[] GetOrCreateKey()
    {
        lock (_lock)
        {
            if (_key is not null)
            {
                return _key;
            }

            if (CredentialManager.ReadCredential(CredentialName)?.Password is { } saved && TryDecode(saved, out var key))
            {
                return _key = key;
            }

            key = RandomNumberGenerator.GetBytes(AesGcmSecretProtector.KeySize);
            CredentialManager.WriteCredential(CredentialName, "OctaGrid", Convert.ToBase64String(key), CredentialPersistence.LocalMachine);
            _logger.LogInformation("Created the key that protects printer secrets");
            return _key = key;
        }
    }

    private static bool TryDecode(string saved, out byte[] key)
    {
        key = [];
        try
        {
            key = Convert.FromBase64String(saved);
        }
        catch (FormatException)
        {
            return false;
        }

        return key.Length == AesGcmSecretProtector.KeySize;
    }
}
