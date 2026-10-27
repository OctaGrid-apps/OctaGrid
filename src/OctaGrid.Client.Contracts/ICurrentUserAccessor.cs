namespace OctaGrid.Client.Contracts;

/// <summary>
/// Resolves which user the app is currently running as. Renamed from "IAuthTokenService" — it
/// never held anything token-shaped (no expiry, no signature), just the current user's id.
/// </summary>
public interface ICurrentUserAccessor
{
    void Clear();

    Guid? GetCurrentUserId();

    void SetCurrentUserId(Guid userId);
}
