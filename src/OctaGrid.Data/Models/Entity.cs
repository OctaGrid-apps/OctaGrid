using OctaGrid.Data.Events;

namespace OctaGrid.Data.Models;

public abstract class Entity
{
    private readonly List<DomainEvent> _domainEvents = [];

    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Optimistic concurrency token. Generated and rotated in app code (see
    /// <see cref="Abstractions.OctaGridContext"/>) rather than a provider-native
    /// mechanism (e.g. Postgres <c>xmin</c>), so behavior is identical across SQLite and Postgres.
    /// </summary>
    public long Version { get; set; }

    /// <summary>Events raised since the entity was last saved. Not mapped: saving moves them to the outbox.</summary>
    public IReadOnlyList<DomainEvent> DomainEvents => _domainEvents;

    /// <summary>Records a fact about this entity, to be stored with the next save.</summary>
    protected void Raise(DomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    internal void ClearDomainEvents() => _domainEvents.Clear();
}
