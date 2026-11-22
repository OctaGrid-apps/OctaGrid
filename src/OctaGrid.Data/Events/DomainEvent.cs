using System.Reflection;
using System.Text.Json.Serialization;

namespace OctaGrid.Data.Events;

/// <summary>
/// A fact an aggregate raised about itself. Raised events are stored in the outbox in the same save as the change
/// that caused them (see <see cref="OctaGridContext"/>) and delivered after it commits, so a handler never sees an
/// event for a change that was not saved.
/// The names below are stored with every event: never change or reuse one.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(PrintJobStarted), "print-job.started")]
[JsonDerivedType(typeof(PrintJobFinished), "print-job.finished")]
[JsonDerivedType(typeof(PrinterAdded), "printer.added")]
[JsonDerivedType(typeof(PrinterChanged), "printer.changed")]
[JsonDerivedType(typeof(PrinterAgentChanged), "printer.agent-changed")]
[JsonDerivedType(typeof(PrinterRemoved), "printer.removed")]
[JsonDerivedType(typeof(WorkspaceRenamed), "workspace.renamed")]
public abstract record DomainEvent
{
    private static readonly IReadOnlyDictionary<Type, string> TypeNames = typeof(DomainEvent)
        .GetCustomAttributes<JsonDerivedTypeAttribute>()
        .ToDictionary(a => a.DerivedType, a => (string)a.TypeDiscriminator!);

    /// <summary>Identifies the event, so a handler that gets it twice can tell.</summary>
    public Guid EventId { get; init; } = Guid.CreateVersion7();

    /// <summary>
    /// When the fact happened. Set by <see cref="OctaGridContext"/> to the save time,
    /// unless the aggregate sets it (for example, the time an agent reported a state).
    /// </summary>
    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>
    /// The <see cref="Models.Entity.Version"/> of the entity that raised the event, as saved with it.
    /// Set by <see cref="OctaGridContext"/>.
    /// </summary>
    public long AggregateVersion { get; init; }

    /// <summary>The stored name of this event's type.</summary>
    [JsonIgnore]
    public string TypeName => TypeNames[GetType()];
}
