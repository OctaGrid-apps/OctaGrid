using OctaGrid.Application.Telemetry;
using OctaGrid.Data.Events;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Application.Events;

/// <summary>
/// Publishes the <see cref="EntityChanged"/> messages clients and agent synchronization rely on,
/// derived from saved domain events, so a message is sent only for a change that was saved.
/// </summary>
public sealed class EntityChangePublisher : IDomainEventHandler
{
    private readonly IMessagePublisher _publisher;

    public EntityChangePublisher(IMessagePublisher publisher)
    {
        _publisher = publisher;
    }

    /// <inheritdoc />
    public Task HandleAsync(DomainEvent domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var change = domainEvent switch
        {
            PrinterAdded e => Printer(e.PrinterId, e.WorkspaceId, ChangeKind.Upserted),
            PrinterChanged e => Printer(e.PrinterId, e.WorkspaceId, ChangeKind.Upserted),
            PrinterAgentChanged e => Printer(e.PrinterId, e.WorkspaceId, ChangeKind.Upserted),
            PrinterRemoved e => Printer(e.PrinterId, e.WorkspaceId, ChangeKind.Deleted),
            WorkspaceRenamed e => new EntityChanged(EntityTypes.Workspace, e.WorkspaceId, e.AggregateVersion, ChangeKind.Upserted, e.WorkspaceId),
            _ => null,
        };

        if (change is not null)
        {
            _publisher.Publish(change);
        }

        return Task.CompletedTask;

        EntityChanged Printer(Guid printerId, Guid workspaceId, ChangeKind kind) =>
            new(EntityTypes.Printer, printerId, domainEvent.AggregateVersion, kind, workspaceId);
    }
}
