using OctaGrid.Data.Events;
using OctaGrid.Data.Models;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Application.Tests;

/// <summary>The print job aggregate: which printer states start and end a job, and the events it raises.</summary>
public sealed class PrintJobTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid PrinterId = Guid.NewGuid();
    private static readonly Guid WorkspaceId = Guid.NewGuid();

    private static PrintJob? Follow(PrintJob? running, UniversalPrinterState state, string? fileName = null, int minutes = 0) =>
        PrintJob.Follow(running, WorkspaceId, PrinterId, state, fileName, At.AddMinutes(minutes));

    private static PrintJob Running(string? fileName = "benchy.gcode") => Follow(null, UniversalPrinterState.Printing, fileName)!;

    [Theory]
    [InlineData(UniversalPrinterState.Printing)]
    [InlineData(UniversalPrinterState.Paused)]
    public void Printing_without_a_running_job_starts_one(UniversalPrinterState state)
    {
        var job = Follow(null, state, "benchy.gcode");

        Assert.NotNull(job);
        Assert.True(job.IsRunning);
        var started = Assert.IsType<PrintJobStarted>(Assert.Single(job.DomainEvents));
        Assert.Equal(new PrintJobStarted(job.Id, WorkspaceId, PrinterId, "benchy.gcode") { EventId = started.EventId, OccurredAt = At }, started);
    }

    [Theory]
    [InlineData(UniversalPrinterState.Idle)]
    [InlineData(UniversalPrinterState.Preparing)]
    [InlineData(UniversalPrinterState.Completed)]
    [InlineData(UniversalPrinterState.Offline)]
    [InlineData(UniversalPrinterState.Connecting)]
    [InlineData(UniversalPrinterState.Error)]
    public void Other_states_without_a_running_job_start_nothing(UniversalPrinterState state)
    {
        Assert.Null(Follow(null, state));
    }

    [Theory]
    [InlineData(UniversalPrinterState.Completed, PrintJobOutcome.Completed)]
    [InlineData(UniversalPrinterState.Failed, PrintJobOutcome.Failed)]
    [InlineData(UniversalPrinterState.Error, PrintJobOutcome.Failed)]
    [InlineData(UniversalPrinterState.Cancelled, PrintJobOutcome.Cancelled)]
    [InlineData(UniversalPrinterState.Idle, PrintJobOutcome.Unknown)]
    public void An_end_state_ends_the_running_job(UniversalPrinterState state, PrintJobOutcome outcome)
    {
        var job = Running();

        Assert.Null(Follow(job, state, minutes: 90));

        Assert.Equal(outcome, job.Outcome);
        Assert.Equal(At.AddMinutes(90), job.EndedAt);
        var finished = Assert.IsType<PrintJobFinished>(job.DomainEvents[^1]);
        Assert.Equal(outcome, finished.Outcome);
        Assert.Equal(At, finished.StartedAt);
        Assert.Equal(At.AddMinutes(90), finished.OccurredAt);
    }

    [Theory]
    [InlineData(UniversalPrinterState.Offline)]
    [InlineData(UniversalPrinterState.Connecting)]
    [InlineData(UniversalPrinterState.Preparing)]
    [InlineData(UniversalPrinterState.Paused)]
    [InlineData(UniversalPrinterState.Printing)]
    public void A_printer_that_drops_off_or_pauses_keeps_its_job_running(UniversalPrinterState state)
    {
        var job = Running();

        Assert.Null(Follow(job, state, "benchy.gcode", minutes: 5));

        Assert.True(job.IsRunning);
        Assert.Single(job.DomainEvents);
    }

    [Fact]
    public void Printing_another_file_ends_the_job_as_unknown_and_starts_a_new_one()
    {
        var job = Running("benchy.gcode");

        var next = Follow(job, UniversalPrinterState.Printing, "boat.gcode", minutes: 30);

        Assert.Equal(PrintJobOutcome.Unknown, job.Outcome);
        Assert.NotNull(next);
        Assert.Equal("boat.gcode", next.FileName);
        Assert.True(next.IsRunning);
    }

    [Fact]
    public void A_file_name_reported_after_the_start_is_kept_on_the_job()
    {
        var job = Running(fileName: null);

        Assert.Null(Follow(job, UniversalPrinterState.Printing, "benchy.gcode", minutes: 1));

        Assert.Equal("benchy.gcode", job.FileName);
        Assert.True(job.IsRunning);
    }

    [Fact]
    public void A_job_cannot_end_twice()
    {
        var job = Running();
        job.Finish(PrintJobOutcome.Completed, At.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => job.Finish(PrintJobOutcome.Failed, At.AddMinutes(2)));
    }

    [Fact]
    public void An_end_reported_before_the_start_by_a_skewed_clock_ends_at_the_start()
    {
        var job = Running();

        job.Finish(PrintJobOutcome.Completed, At.AddMinutes(-5));

        Assert.Equal(At, job.EndedAt);
    }
}
