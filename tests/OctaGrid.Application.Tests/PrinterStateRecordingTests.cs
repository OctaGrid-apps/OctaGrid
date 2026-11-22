using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OctaGrid.Application.Agents;
using OctaGrid.Application.Events;
using OctaGrid.Application.Services;
using OctaGrid.Data;
using OctaGrid.Data.Events;
using OctaGrid.Data.Models;
using OctaGrid.Extensions.Models;
using OctaGrid.Protocol.AgentLink.Notifications;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Application.Tests;

/// <summary>
/// From an agent's state change to a delivered domain event: the recorder turns changes into print jobs, the jobs'
/// events are saved to the outbox with them, and the dispatcher hands them to the handlers in order.
/// </summary>
public sealed class PrinterStateRecordingTests : IAsyncDisposable
{
    private static readonly DateTimeOffset At = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    private readonly AgentLinkFixture _fixture = new();
    private readonly RecordingHandler _handler = new();
    private readonly ServiceProvider _services;

    public PrinterStateRecordingTests()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _fixture.NewContext());
        services.AddScoped<PrinterStateService>();
        services.AddSingleton<IDomainEventHandler>(_handler);
        services.AddSingleton<IAgentLink>(_fixture.Link);
        services.AddSingleton<OutboxDispatcher>();
        services.AddSingleton<PrinterStateRecorder>();
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        _fixture.Dispose();
    }

    private OutboxDispatcher Outbox => _services.GetRequiredService<OutboxDispatcher>();

    private PrinterStateRecorder Recorder => _services.GetRequiredService<PrinterStateRecorder>();

    private async Task Report(Guid printerId, UniversalPrinterState state, string? fileName = null, int minutes = 0, Guid? agentId = null)
    {
        _fixture.Link.Notify(agentId ?? _fixture.AgentId, new PrinterStateChanged(printerId, null, state, fileName, At.AddMinutes(minutes)));
        await Recorder.RecordQueuedAsync(CancellationToken.None);
    }

    private List<PrintJob> Jobs()
    {
        using var context = _fixture.NewContext();
        return context.PrintJobs.AsNoTracking().ToList();
    }

    private List<OutboxMessage> Outboxed()
    {
        using var context = _fixture.NewContext();
        return context.OutboxMessages.AsNoTracking().OrderBy(m => m.Sequence).ToList();
    }

    [Fact]
    public async Task A_printer_that_starts_printing_gets_a_running_job_and_its_event_waits_in_the_outbox()
    {
        var printer = await _fixture.CreatePrinter();
        using var listening = Recorder.Start();

        await Report(printer.Id, UniversalPrinterState.Printing, "benchy.gcode");

        var job = Assert.Single(Jobs());
        Assert.Equal(printer.Id, job.PrinterId);
        Assert.Equal(_fixture.WorkspaceId, job.WorkspaceId);
        Assert.Equal("benchy.gcode", job.FileName);
        Assert.True(job.IsRunning);
        var message = Assert.Single(Outboxed());
        Assert.Equal("print-job.started", message.Type);
        Assert.Null(message.ProcessedAt);
        Assert.Equal(job.Id, Assert.IsType<PrintJobStarted>(message.ToEvent()).PrintJobId);
    }

    [Fact]
    public async Task A_finished_print_is_delivered_to_the_handlers_as_started_then_finished()
    {
        var printer = await _fixture.CreatePrinter();
        using var listening = Recorder.Start();

        await Report(printer.Id, UniversalPrinterState.Printing, "benchy.gcode");
        await Report(printer.Id, UniversalPrinterState.Completed, "benchy.gcode", minutes: 95);
        var drained = await Outbox.DispatchPendingAsync(CancellationToken.None);

        Assert.True(drained);
        Assert.Equal(PrintJobOutcome.Completed, Assert.Single(Jobs()).Outcome);
        Assert.Collection(_handler.Received,
            e => Assert.IsType<PrintJobStarted>(e),
            e => Assert.Equal(PrintJobOutcome.Completed, Assert.IsType<PrintJobFinished>(e).Outcome));
        Assert.All(Outboxed(), m => Assert.NotNull(m.ProcessedAt));
    }

    [Fact]
    public async Task Delivered_events_are_not_delivered_again()
    {
        var printer = await _fixture.CreatePrinter();
        using var listening = Recorder.Start();
        await Report(printer.Id, UniversalPrinterState.Printing);
        await Outbox.DispatchPendingAsync(CancellationToken.None);

        await Outbox.DispatchPendingAsync(CancellationToken.None);

        Assert.Single(_handler.Received);
    }

    [Fact]
    public async Task A_state_reported_by_an_agent_that_does_not_run_the_printer_is_ignored()
    {
        var printer = await _fixture.CreatePrinter();
        var foreignAgent = _fixture.AddForeignAgent();
        using var listening = Recorder.Start();

        await Report(printer.Id, UniversalPrinterState.Printing, agentId: foreignAgent);

        Assert.Empty(Jobs());
        Assert.Empty(Outboxed());
    }

    [Fact]
    public async Task A_state_that_changes_no_job_starts_none_and_raises_no_event()
    {
        var printer = await _fixture.CreatePrinter();
        using var listening = Recorder.Start();

        await Report(printer.Id, UniversalPrinterState.Idle);
        await Report(printer.Id, UniversalPrinterState.Offline);

        Assert.Empty(Jobs());
        Assert.Empty(Outboxed());
    }

    [Fact]
    public async Task The_last_reported_state_is_kept_and_returned_with_the_printer_without_changing_its_version()
    {
        var printer = await _fixture.CreatePrinter();
        using var listening = Recorder.Start();

        await Report(printer.Id, UniversalPrinterState.Printing, minutes: 1);
        await Report(printer.Id, UniversalPrinterState.Offline, minutes: 2);

        await using var context = _fixture.NewContext();
        var read = await _fixture.Printers(context).GetPrinter(_fixture.UserId, _fixture.WorkspaceId, printer.Id, CancellationToken.None);
        Assert.Equal(UniversalPrinterState.Offline, read.State);
        Assert.Equal(printer.Version, read.Version);
        var status = (await context.Printers.Include(p => p.Status).SingleAsync(p => p.Id == printer.Id, TestContext.Current.CancellationToken)).Status!;
        Assert.Equal(At.AddMinutes(2), status.ObservedAt);
        Assert.Equal(At.AddMinutes(1), status.LastSeenAt);
    }

    [Fact]
    public async Task A_report_older_than_the_kept_state_does_not_replace_it()
    {
        var printer = await _fixture.CreatePrinter();
        using var listening = Recorder.Start();

        await Report(printer.Id, UniversalPrinterState.Idle, minutes: 5);
        await Report(printer.Id, UniversalPrinterState.Offline, minutes: 1);

        await using var context = _fixture.NewContext();
        var read = await _fixture.Printers(context).GetPrinter(_fixture.UserId, _fixture.WorkspaceId, printer.Id, CancellationToken.None);
        Assert.Equal(UniversalPrinterState.Idle, read.State);
    }

    [Fact]
    public async Task A_printer_no_agent_has_reported_on_has_no_state()
    {
        var printer = await _fixture.CreatePrinter();

        Assert.Null(printer.State);
    }

    [Fact]
    public async Task A_failed_delivery_keeps_the_event_and_those_after_it_for_the_next_try_in_order()
    {
        var printer = await _fixture.CreatePrinter();
        using var listening = Recorder.Start();
        await Report(printer.Id, UniversalPrinterState.Printing);
        await Report(printer.Id, UniversalPrinterState.Cancelled, minutes: 3);
        _handler.FailNext = 1;

        var drained = await Outbox.DispatchPendingAsync(CancellationToken.None);

        Assert.False(drained);
        Assert.Empty(_handler.Received);
        var failed = Outboxed()[0];
        Assert.Equal(1, failed.Attempts);
        Assert.Contains("Handler failed", failed.LastError);
        Assert.All(Outboxed(), m => Assert.Null(m.ProcessedAt));

        Assert.True(await Outbox.DispatchPendingAsync(CancellationToken.None));
        Assert.Collection(_handler.Received, e => Assert.IsType<PrintJobStarted>(e), e => Assert.IsType<PrintJobFinished>(e));
    }

    [Fact]
    public async Task An_event_that_keeps_failing_is_left_behind_so_the_ones_after_it_go_out()
    {
        var printer = await _fixture.CreatePrinter();
        using var listening = Recorder.Start();
        await Report(printer.Id, UniversalPrinterState.Printing);
        await Report(printer.Id, UniversalPrinterState.Completed, minutes: 3);
        _handler.FailNext = OutboxDispatcher.MaxAttempts;

        for (var attempt = 0; attempt < OutboxDispatcher.MaxAttempts; attempt++)
        {
            Assert.False(await Outbox.DispatchPendingAsync(CancellationToken.None));
        }

        Assert.True(await Outbox.DispatchPendingAsync(CancellationToken.None));

        Assert.IsType<PrintJobFinished>(Assert.Single(_handler.Received));
        var stuck = Outboxed()[0];
        Assert.Null(stuck.ProcessedAt);
        Assert.Equal(OutboxDispatcher.MaxAttempts, stuck.Attempts);
    }

    [Fact]
    public async Task The_running_dispatcher_delivers_what_waited_and_what_is_recorded_later()
    {
        var printer = await _fixture.CreatePrinter();
        using var listening = Recorder.Start();
        await Report(printer.Id, UniversalPrinterState.Printing);
        using var stop = new CancellationTokenSource();
        var running = Outbox.RunAsync(stop.Token);

        await _handler.WaitForAsync(1);
        // Recording signals the dispatcher.
        await Report(printer.Id, UniversalPrinterState.Completed, minutes: 3);
        await _handler.WaitForAsync(2);

        await stop.CancelAsync();
        await running;
        Assert.Collection(_handler.Received, e => Assert.IsType<PrintJobStarted>(e), e => Assert.IsType<PrintJobFinished>(e));
    }

    [Fact]
    public async Task Deleting_a_printer_deletes_its_jobs()
    {
        var printer = await _fixture.CreatePrinter();
        using var listening = Recorder.Start();
        await Report(printer.Id, UniversalPrinterState.Printing);

        await using (var context = _fixture.NewContext())
        {
            await _fixture.Printers(context).Delete(_fixture.UserId, printer.Id, CancellationToken.None);
        }

        Assert.Empty(Jobs());
    }

    /// <summary>Keeps what it gets and can be told to fail, as a handler whose own store is down.</summary>
    private sealed class RecordingHandler : IDomainEventHandler
    {
        private readonly List<DomainEvent> _received = [];
        private readonly List<(int Count, TaskCompletionSource Done)> _waiters = [];

        public int FailNext { get; set; }

        public IReadOnlyList<DomainEvent> Received
        {
            get
            {
                lock (_received)
                {
                    return _received.ToList();
                }
            }
        }

        public Task HandleAsync(DomainEvent domainEvent, CancellationToken cancellationToken)
        {
            lock (_received)
            {
                if (FailNext > 0)
                {
                    FailNext--;
                    throw new InvalidOperationException("Handler failed");
                }

                _received.Add(domainEvent);
                foreach (var waiter in _waiters.Where(w => _received.Count >= w.Count))
                {
                    waiter.Done.TrySetResult();
                }
            }

            return Task.CompletedTask;
        }

        public Task WaitForAsync(int count)
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_received)
            {
                if (_received.Count >= count)
                {
                    return Task.CompletedTask;
                }

                _waiters.Add((count, done));
            }

            // Only a guard against a hang: the handler completes the wait as soon as the event arrives.
            return done.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }
}
