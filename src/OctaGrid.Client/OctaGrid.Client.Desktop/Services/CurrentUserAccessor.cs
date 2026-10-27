using Meziantou.Framework.Win32;
using OctaGrid.Client.Contracts;

namespace OctaGrid.Client.Desktop.Services;

internal sealed class CurrentUserAccessor : ICurrentUserAccessor
{
    private const string ApplicationName = nameof(OctaGrid);

    public void Clear()
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600))
        {
            CredentialManager.DeleteCredential(ApplicationName);
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
            return credential is not null && Guid.TryParse(credential.Password, out var userId) ? userId : null;
        }

        throw new PlatformNotSupportedException();
    }

    public void SetCurrentUserId(Guid userId)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600))
        {
            CredentialManager.WriteCredential(ApplicationName, "", userId.ToString(), CredentialPersistence.LocalMachine);
        }
        else
        {
            throw new PlatformNotSupportedException();
        }
    }
}
