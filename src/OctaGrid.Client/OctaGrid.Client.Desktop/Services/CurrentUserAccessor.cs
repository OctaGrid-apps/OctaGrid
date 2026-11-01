using Meziantou.Framework.Win32;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Contracts;

namespace OctaGrid.Client.Desktop.Services;

internal sealed class CurrentUserAccessor : ICurrentUserAccessor
{
    private const string ApplicationName = nameof(OctaGrid);
    private readonly ILogger<CurrentUserAccessor> _logger;
    private readonly object _lock = new();
    private Guid? _cachedUserId;
    private bool _hasReadCredential;

    public CurrentUserAccessor(ILogger<CurrentUserAccessor>? logger = null)
    {
        _logger = logger ?? NullLogger<CurrentUserAccessor>.Instance;
    }

    public void Clear()
    {
        lock (_lock)
        {
            _cachedUserId = null;
            _hasReadCredential = true;
        }

        if (OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600))
        {
            CredentialManager.DeleteCredential(ApplicationName);
            _logger.LogInformation("Cleared stored user credential for {ApplicationName}", ApplicationName);
        }
        else
        {
            throw new PlatformNotSupportedException();
        }
    }

    public Guid? GetCurrentUserId()
    {
        lock (_lock)
        {
            if (_hasReadCredential)
            {
                return _cachedUserId;
            }
        }

        if (OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600))
        {
            var credential = CredentialManager.ReadCredential(ApplicationName);
            lock (_lock)
            {
                _hasReadCredential = true;
                if (credential is not null && Guid.TryParse(credential.Password, out var userId))
                {
                    _cachedUserId = userId;
                    return userId;
                }
            }

            if (credential is not null)
            {
                _logger.LogWarning("Stored credential for {ApplicationName} did not contain a valid UserId GUID", ApplicationName);
            }
            return null;
        }

        throw new PlatformNotSupportedException();
    }

    public void SetCurrentUserId(Guid userId)
    {
        lock (_lock)
        {
            _cachedUserId = userId;
            _hasReadCredential = true;
        }

        if (OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600))
        {
            CredentialManager.WriteCredential(ApplicationName, "", userId.ToString(), CredentialPersistence.LocalMachine);
            _logger.LogDebug("Stored current user credential ({UserId}) for {ApplicationName}", userId, ApplicationName);
        }
        else
        {
            throw new PlatformNotSupportedException();
        }
    }
}
