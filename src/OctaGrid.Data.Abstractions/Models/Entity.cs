namespace OctaGrid.Data.Abstractions.Models;

public abstract class Entity
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Optimistic concurrency token. Generated and rotated in app code (see
    /// <see cref="Abstractions.OctaGridContext"/>) rather than a provider-native
    /// mechanism (e.g. Postgres <c>xmin</c>), so behavior is identical across SQLite and Postgres.
    /// </summary>
    public long Version { get; set; }
}
