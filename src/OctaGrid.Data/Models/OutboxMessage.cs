using System.Text.Json;
using OctaGrid.Data.Events;

namespace OctaGrid.Data.Models;

/// <summary>
/// A <see cref="DomainEvent"/> waiting to be delivered, saved in the same transaction as the change that raised it.
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>The order events are delivered in. Assigned by the database.</summary>
    public long Sequence { get; private set; }

    public Guid EventId { get; private set; }

    /// <summary>The event's stored type name (<see cref="DomainEvent.TypeName"/>).</summary>
    public string Type { get; private set; } = string.Empty;

    /// <summary>The event as JSON, with its type name, as <see cref="ToEvent"/> reads it.</summary>
    public string Payload { get; private set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>When every handler took the event; null while it waits.</summary>
    public DateTimeOffset? ProcessedAt { get; private set; }

    /// <summary>How many times delivery failed.</summary>
    public int Attempts { get; private set; }

    /// <summary>Why the last delivery failed. Holds no event data.</summary>
    public string? LastError { get; private set; }

    // For EF.
    private OutboxMessage()
    {
    }

    public static OutboxMessage From(DomainEvent domainEvent) => new()
    {
        EventId = domainEvent.EventId,
        Type = domainEvent.TypeName,
        Payload = JsonSerializer.Serialize(domainEvent),
        OccurredAt = domainEvent.OccurredAt,
    };

    public DomainEvent ToEvent() =>
        JsonSerializer.Deserialize<DomainEvent>(Payload)
        ?? throw new JsonException($"Outbox message {Sequence} holds no event.");

    public void MarkProcessed(DateTimeOffset at)
    {
        ProcessedAt = at;
        LastError = null;
    }

    public void MarkFailed(string error)
    {
        Attempts++;
        LastError = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
    }

    public const int MaxErrorLength = 1000;
}
