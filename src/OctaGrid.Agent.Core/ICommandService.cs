namespace OctaGrid.Agent.Core;

public interface ICommandService
{
    Task TriggerInitialStateAsync(Guid printerId, CancellationToken cancellationToken);

    Task TriggerInitialStateAsync(IEnumerable<Guid> printers, CancellationToken cancellationToken);
}
