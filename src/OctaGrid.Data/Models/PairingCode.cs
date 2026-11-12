namespace OctaGrid.Data.Models;

public sealed class PairingCode
{
    public Guid Id { get; private set; }

    /// <summary>SHA-256 hash of the normalized code. Never store the plaintext code.</summary>
    public byte[] CodeHash { get; private set; } = default!;

    /// <summary>The authenticated account/user that requested this code — required so a device
    /// can only ever pair into the account that generated the code, never a different one.</summary>
    public Guid AccountId { get; private set; }

    /// <summary>Set once a device claims this code.</summary>
    public Guid? DeviceId { get; private set; }

    public bool Consumed { get; private set; }

    public int AttemptCount { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    // EF requires a parameterless/protected constructor
    private PairingCode()
    { }

    public static PairingCode Create(byte[] codeHash, Guid accountId, TimeSpan ttl)
    {
        return new PairingCode
        {
            Id = Guid.CreateVersion7(),
            CodeHash = codeHash,
            AccountId = accountId,
            DeviceId = null,
            Consumed = false,
            AttemptCount = 0,
            ExpiresAt = DateTimeOffset.UtcNow.Add(ttl),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public bool IsUsable(DateTimeOffset now, int maxAttempts = 5) =>
        !Consumed && now < ExpiresAt && AttemptCount < maxAttempts;

    public void RegisterFailedAttempt() => AttemptCount++;

    public void MarkConsumed(Guid deviceId)
    {
        Consumed = true;
        DeviceId = deviceId;
    }
}
