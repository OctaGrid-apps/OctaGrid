using Meziantou.Framework.Win32;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Contracts;

namespace OctaGrid.Client.Desktop.Services;

internal sealed class CurrentUserAccessor : ICurrentUserAccessor
{
    private const string ApplicationName = nameof(OctaGrid);
    private readonly ILogger<CurrentUserAccessor> _logger;

    public CurrentUserAccessor(ILogger<CurrentUserAccessor>? logger = null)
    {
        _logger = logger ?? NullLogger<CurrentUserAccessor>.Instance;
    }

    public void Clear()
    {
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
        if (OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600))
        {
            var credential = CredentialManager.ReadCredential(ApplicationName);
            if (credential is null)
            {
                return null;
            }

            if (Guid.TryParse(credential.Password, out var userId))
            {
                return userId;
            }

            _logger.LogWarning("Stored credential for {ApplicationName} did not contain a valid UserId GUID", ApplicationName);
            return null;
        }

        throw new PlatformNotSupportedException();
    }

    public void SetCurrentUserId(Guid userId)
    {
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
