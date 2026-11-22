using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Data;
using OctaGrid.Data.Models;

namespace OctaGrid.Application.Events;

/// <summary>
/// Delivers the <see cref="OutboxMessage"/>s that saves left behind to the <see cref="IDomainEventHandler"/>s, in the
/// order they were saved. Runs for the backend's lifetime (<see cref="RunAsync"/>); whoever saves events calls
/// <see cref="Signal"/> so they go out at once, and anything left after a failure is tried again after
/// <see cref="RetryDelay"/>.
/// </summary>
public sealed class OutboxDispatcher : IOutboxSignal
{
    /// <summary>How many failed deliveries an event gets before it is left in the outbox for someone to look at.</summary>
    public const int MaxAttempts = 5;

    /// <summary>How long a failed delivery waits before it is tried again, unless new events come first.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    private const int BatchSize = 100;

    private readonly ILogger<OutboxDispatcher> _logger;
    private readonly IServiceScopeFactory _scopes;
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly TimeProvider _time;

    // A wait for the signal that lost to the retry delay stays here for the next round instead of being dropped: an
    // abandoned wait would still take the next signal, and that signal would be lost.
    private Task? _signalled;

    public OutboxDispatcher(IServiceScopeFactory scopes, TimeProvider? time = null, ILogger<OutboxDispatcher>? logger = null)
    {
        _scopes = scopes;
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger<OutboxDispatcher>.Instance;
    }

    /// <summary>Tells the dispatcher there are new events. Never blocks.</summary>
    public void Signal()
    {
        // At most one pending signal: one pass picks up everything saved before it.
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    /// <summary>Delivers what waits now and whenever signalled, until cancelled.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var complete = false;
            try
            {
                complete = await DispatchPendingAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not read the outbox; trying again in {RetryDelay}", RetryDelay);
            }

            try
            {
                await WaitAsync(retry: !complete, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task WaitAsync(bool retry, CancellationToken cancellationToken)
    {
        if (_signalled is null || _signalled.IsCompleted)
        {
            _signalled = _signal.WaitAsync(cancellationToken);
        }

        if (!retry)
        {
            await _signalled;
            return;
        }

        using var delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delay = Task.Delay(RetryDelay, _time, delayCancellation.Token);
        var first = await Task.WhenAny(_signalled, delay);
        await delayCancellation.CancelAsync();
        await first;
    }

    /// <summary>Delivers every waiting event, in order, stopping at the first one a handler fails.</summary>
    /// <returns>Whether nothing is left to deliver.</returns>
    public async Task<bool> DispatchPendingAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await using var scope = _scopes.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<OctaGridContext>();
            var handlers = scope.ServiceProvider.GetServices<IDomainEventHandler>().ToList();

            var batch = await context.OutboxMessages
                .Where(m => m.ProcessedAt == null && m.Attempts < MaxAttempts)
                .OrderBy(m => m.Sequence)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
            {
                return true;
            }

            foreach (var message in batch)
            {
                if (!await DeliverAsync(message, handlers, cancellationToken))
                {
                    await context.SaveChangesAsync(cancellationToken);
                    return false;
                }
            }

            await context.SaveChangesAsync(cancellationToken);
            if (batch.Count < BatchSize)
            {
                return true;
            }
        }
    }

    private async Task<bool> DeliverAsync(OutboxMessage message, List<IDomainEventHandler> handlers, CancellationToken cancellationToken)
    {
        try
        {
            var domainEvent = message.ToEvent();
            foreach (var handler in handlers)
            {
                await handler.HandleAsync(domainEvent, cancellationToken);
            }

            message.MarkProcessed(_time.GetUtcNow());
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Only the exception type and message: the event itself may name files the user printed.
            message.MarkFailed($"{ex.GetType().Name}: {ex.Message}");
            _logger.LogError(ex, "Delivering event {EventId} ({EventType}) failed, attempt {Attempt} of {MaxAttempts}",
                message.EventId, message.Type, message.Attempts, MaxAttempts);
            return false;
        }
    }
}
