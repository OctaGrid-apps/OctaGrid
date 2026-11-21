using OctaGrid.Data.Events;

namespace OctaGrid.Application.Events;

/// <summary>
/// Reacts to saved <see cref="DomainEvent"/>s. Registered as scoped; the <see cref="OutboxDispatcher"/> hands every
/// event to every handler, in the order the events were saved, and ignores the types a handler does not handle.
/// </summary>
/// <remarks>
/// Delivery is at least once: when one handler fails, the event is offered to every handler again later. Handlers use
/// <see cref="DomainEvent.EventId"/> to recognise one they have already handled.
/// </remarks>
public interface IDomainEventHandler
{
    Task HandleAsync(DomainEvent domainEvent, CancellationToken cancellationToken);
}
